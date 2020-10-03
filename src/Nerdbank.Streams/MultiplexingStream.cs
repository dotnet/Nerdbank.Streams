// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using MessagePack;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// Encodes multiple channels over a single transport.
    /// </summary>
    public partial class MultiplexingStream : IDisposableObservable, System.IAsyncDisposable
    {
        /// <summary>
        /// The channel id reserved for control frames.
        /// </summary>
        private const int ControlChannelId = 0;

        /// <summary>
        /// The maximum length of a frame's payload.
        /// </summary>
        private const int FramePayloadMaxLength = 20 * 1024;

        /// <summary>
        /// The encoding used for characters in control frames.
        /// </summary>
        private static readonly Encoding ControlFrameEncoding = Encoding.UTF8;

        /// <summary>
        /// The options to use for channels we create in response to incoming offers.
        /// </summary>
        /// <remarks>
        /// Whatever these settings are, they can be replaced when the channel is accepted.
        /// </remarks>
        private static readonly ChannelOptions DefaultChannelOptions = new ChannelOptions();

        /// <summary>
        /// A value indicating whether this is the "odd" party in the conversation (where the other one would be "even").
        /// </summary>
        /// <remarks>
        /// This value is only significant for parts of the protocol where it's useful to have the two parties behave slightly differently to avoid conflicts.
        /// </remarks>
        private readonly bool? isOdd;

        /// <summary>
        /// The formatter to use for serializing/deserializing multiplexing streams.
        /// </summary>
        private readonly Formatter formatter;

        /// <summary>
        /// The object to lock when accessing internal fields.
        /// </summary>
        private readonly object syncObject = new object();

        /// <summary>
        /// A dictionary of channels being offered by the remote end but not yet accepted by us, keyed by name.
        /// This does not include ephemeral channels (those without a name).
        /// </summary>
        private readonly Dictionary<string, Queue<Channel>> channelsOfferedByThemByName = new Dictionary<string, Queue<Channel>>(StringComparer.Ordinal);

        /// <summary>
        /// A dictionary of channels being accepted (but not yet offered).
        /// </summary>
        private readonly Dictionary<string, Queue<TaskCompletionSource<Channel>>> acceptingChannels = new Dictionary<string, Queue<TaskCompletionSource<Channel>>>(StringComparer.Ordinal);

        /// <summary>
        /// A dictionary of all open channels (including those not yet accepted), keyed by their ID.
        /// </summary>
        private readonly Dictionary<QualifiedChannelId, Channel> openChannels = new Dictionary<QualifiedChannelId, Channel>();

        /// <summary>
        /// Contains the set of channels for which we have transmitted a <see cref="ControlCode.ChannelTerminated"/> frame
        /// but for which we have not received the same frame.
        /// </summary>
        private readonly HashSet<QualifiedChannelId> channelsPendingTermination = new HashSet<QualifiedChannelId>();

        /// <summary>
        /// A semaphore that must be entered to write to the underlying <see cref="formatter"/>.
        /// </summary>
        private readonly SemaphoreSlim sendingSemaphore = new SemaphoreSlim(1);

        /// <summary>
        /// The source for the <see cref="Completion"/> property.
        /// </summary>
        private readonly TaskCompletionSource<object?> completionSource = new TaskCompletionSource<object?>();

        /// <summary>
        /// A token that is canceled when this instance is disposed.
        /// </summary>
        private readonly CancellationTokenSource disposalTokenSource = new CancellationTokenSource();

        /// <summary>
        /// The major version of the protocol being used for this connection.
        /// </summary>
        private readonly int protocolMajorVersion;

        /// <summary>
        /// The last number assigned to a channel.
        /// Each use of this should increment by two, if <see cref="isOdd"/> has a value.
        /// </summary>
        private long lastOfferedChannelId;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class.
        /// </summary>
        /// <param name="formatter">The formatter to use for the multiplexing frames.</param>
        /// <param name="isOdd">A value indicating whether this party is the "odd" one. No value indicates this is the newer protocol version that no longer requires it.</param>
        /// <param name="options">The options for this instance.</param>
        private MultiplexingStream(Formatter formatter, bool? isOdd, Options options)
        {
            this.formatter = formatter;
            this.isOdd = isOdd;
            if (isOdd.HasValue)
            {
                this.lastOfferedChannelId = isOdd.Value ? -1 : 0; // the first channel created should be 1 or 2
            }

            this.TraceSource = options.TraceSource;

            this.DefaultChannelTraceSourceFactory =
                options.DefaultChannelTraceSourceFactoryWithQualifier
#pragma warning disable CS0618 // Type or member is obsolete
                ?? (options.DefaultChannelTraceSourceFactory is { } fac ? new Func<QualifiedChannelId, string, TraceSource?>((id, name) => fac(checked((int)id.Id), name)) : null);
#pragma warning restore CS0618 // Type or member is obsolete

            this.DefaultChannelReceivingWindowSize = options.DefaultChannelReceivingWindowSize;
            this.protocolMajorVersion = options.ProtocolMajorVersion;

            // Set up seed channels
            for (int i = 0; i < options.SeededChannels.Count; i++)
            {
                var id = new QualifiedChannelId((ulong)i, ChannelSource.Seeded);
                this.openChannels.Add(id, new Channel(this, id, new Channel.OfferParameters(string.Empty, options.SeededChannels[i]?.ChannelReceivingWindowSize ?? this.DefaultChannelReceivingWindowSize)));
            }

            this.lastOfferedChannelId += options.SeededChannels.Count;

            // Initiate reading from the transport stream. This will not end until the stream does, or we're disposed.
            // If reading the stream fails, we'll dispose ourselves.
            this.DisposeSelfOnFailure(this.ReadStreamAsync());
        }

        /// <summary>
        /// Occurs when the remote party offers to establish a channel.
        /// </summary>
        public event EventHandler<ChannelOfferEventArgs>? ChannelOffered;

        private enum TraceEventId
        {
            HandshakeSuccessful = 1,
            HandshakeFailed,
            FatalError,
            UnexpectedChannelAccept,
            ChannelAutoClosing,
            StreamDisposed,
            AcceptChannelWaiting,
            AcceptChannelAlreadyOffered,
            AcceptChannelCanceled,
            ChannelOfferReceived,
            ChannelDisposed,
            OfferChannelCanceled,
            FrameSent,
            FrameReceived,
            FrameSentPayload,
            FrameReceivedPayload,

            /// <summary>
            /// Raised when content arrives for a channel that has been disposed locally, resulting in discarding the content.
            /// </summary>
            ContentDiscardedOnDisposedChannel,

            /// <summary>
            /// Raised when we are about to read (or wait for) the next frame.
            /// </summary>
            WaitingForNextFrame,

            /// <summary>
            /// Raised when we receive a <see cref="ControlCode.ContentProcessed"/> message for an unknown or closed channel.
            /// </summary>
            UnexpectedContentProcessed,

            /// <summary>
            /// Raised when the protocol handshake is starting, to annouce the major version being used.
            /// </summary>
            HandshakeStarted,
        }

        /// <summary>
        /// Gets a task that completes when this instance is disposed, and may have captured a fault that led to its self-disposal.
        /// </summary>
        public Task Completion => this.completionSource.Task;

        /// <summary>
        /// Gets the logger used by this instance.
        /// </summary>
        /// <value>Never null.</value>
        public TraceSource TraceSource { get; }

        /// <summary>
        /// Gets the default window size used for new channels that do not specify a value for <see cref="ChannelOptions.ChannelReceivingWindowSize"/>.
        /// </summary>
        /// <remarks>
        /// This value can be set at the time of creation of the <see cref="MultiplexingStream"/> via <see cref="Options.DefaultChannelReceivingWindowSize"/>.
        /// </remarks>
        public long DefaultChannelReceivingWindowSize { get; }

        /// <inheritdoc />
        bool IDisposableObservable.IsDisposed => this.Completion.IsCompleted;

        /// <summary>
        /// Gets a token that is canceled when this instance is disposed.
        /// </summary>
        internal CancellationToken DisposalToken => this.disposalTokenSource.Token;

        /// <summary>
        /// Gets a factory for <see cref="TraceSource"/> instances to attach to a newly opened <see cref="Channel"/>
        /// when its <see cref="ChannelOptions.TraceSource"/> is <c>null</c>.
        /// </summary>
        private Func<QualifiedChannelId, string, TraceSource?>? DefaultChannelTraceSourceFactory { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class
        /// with <see cref="Options.ProtocolMajorVersion"/> set to 3.
        /// </summary>
        /// <param name="stream">The stream to multiplex multiple channels over. Use <see cref="FullDuplexStream.Splice(Stream, Stream)"/> if you have distinct input/output streams.</param>
        /// <param name="options">Options to define behavior for the multiplexing stream.</param>
        /// <returns>The multiplexing stream.</returns>
        public static MultiplexingStream Create(Stream stream, Options? options = null)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanRead, nameof(stream), "Stream must be readable.");
            Requires.Argument(stream.CanWrite, nameof(stream), "Stream must be writable.");

            options = options ?? new Options { ProtocolMajorVersion = 3 };

            var streamWriter = stream.UsePipeWriter();

            var formatter = options.ProtocolMajorVersion switch
            {
                // We do NOT support 1-2 here because they require an asynchronous handshake.
                3 => new V3Formatter(streamWriter, stream),
                _ => throw new NotSupportedException($"Protocol major version {options.ProtocolMajorVersion} is not supported."),
            };

            return new MultiplexingStream(formatter, isOdd: null, options);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class.
        /// </summary>
        /// <param name="stream">The stream to multiplex multiple channels over. Use <see cref="FullDuplexStream.Splice(Stream, Stream)"/> if you have distinct input/output streams.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The multiplexing stream, once the handshake is complete.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the remote end disconnects before the handshake is complete.</exception>
        public static Task<MultiplexingStream> CreateAsync(Stream stream, CancellationToken cancellationToken = default) => CreateAsync(stream, options: null, cancellationToken);

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class.
        /// </summary>
        /// <param name="stream">The stream to multiplex multiple channels over. Use <see cref="FullDuplexStream.Splice(Stream, Stream)"/> if you have distinct input/output streams.</param>
        /// <param name="options">Options to define behavior for the multiplexing stream. If unspecified, the default options will include <see cref="Options.ProtocolMajorVersion"/> set to 1.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The multiplexing stream, once the handshake is complete.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the remote end disconnects before the handshake is complete.</exception>
        public static async Task<MultiplexingStream> CreateAsync(Stream stream, Options? options, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanRead, nameof(stream), "Stream must be readable.");
            Requires.Argument(stream.CanWrite, nameof(stream), "Stream must be writable.");

            options = options ?? new Options();
            if (options.ProtocolMajorVersion < 3 && options.SeededChannels.Count > 0)
            {
                throw new NotSupportedException(Strings.SeededChannelsRequireV3Protocol);
            }

            var streamWriter = stream.UsePipeWriter(cancellationToken: cancellationToken);

            var formatter = options.ProtocolMajorVersion switch
            {
                1 => (Formatter)new V1Formatter(streamWriter, stream),
                2 => new V2Formatter(streamWriter, stream),
                3 => new V3Formatter(streamWriter, stream),
                _ => throw new NotSupportedException($"Protocol major version {options.ProtocolMajorVersion} is not supported."),
            };

            try
            {
                if (options.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    options.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.HandshakeStarted, $"Multiplexing protocol handshake beginning with major version {options.ProtocolMajorVersion}.");
                }

                // Send the protocol magic number, and a random GUID to establish even/odd assignments.
                object? handshakeData = formatter.WriteHandshake();
                await formatter.FlushAsync(cancellationToken).ConfigureAwait(false);

                var handshakeResult = await formatter.ReadHandshakeAsync(handshakeData, options, cancellationToken).ConfigureAwait(false);

                if (options.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    options.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.HandshakeSuccessful, "Multiplexing protocol established successfully.");
                }

                return new MultiplexingStream(formatter, handshakeResult.IsOdd, options);
            }
            catch (Exception ex)
            {
                if (options.TraceSource.Switch.ShouldTrace(TraceEventType.Critical))
                {
                    options.TraceSource.TraceEvent(TraceEventType.Critical, (int)TraceEventId.HandshakeFailed, ex.Message);
                }

                throw;
            }
        }

        /// <summary>
        /// Creates an anonymous channel that may be accepted by <see cref="AcceptChannel(int, ChannelOptions)"/>.
        /// Its existance must be communicated by other means (typically another, existing channel) to encourage acceptance.
        /// </summary>
        /// <param name="options">A set of options that describe local treatment of this channel.</param>
        /// <returns>The anonymous channel.</returns>
        /// <remarks>
        /// Note that while the channel is created immediately, any local write to that channel will be buffered locally
        /// until the remote party accepts the channel.
        /// </remarks>
        public Channel CreateChannel(ChannelOptions? options = default)
        {
            Verify.NotDisposed(this);

            var offerParameters = new Channel.OfferParameters(string.Empty, options?.ChannelReceivingWindowSize ?? this.DefaultChannelReceivingWindowSize);
            var payload = this.formatter.Serialize(offerParameters);

            var qualifiedId = new QualifiedChannelId(this.GetUnusedChannelId(), ChannelSource.Local);
            Channel channel = new Channel(this, qualifiedId, offerParameters, options ?? DefaultChannelOptions);
            lock (this.syncObject)
            {
                this.openChannels.Add(qualifiedId, channel);
            }

            var header = new FrameHeader
            {
                Code = ControlCode.Offer,
                ChannelId = qualifiedId,
            };

            this.SendFrame(header, payload, this.DisposalToken);
            return channel;
        }

        /// <inheritdoc cref="AcceptChannel(ulong, ChannelOptions?)"/>
        public Channel AcceptChannel(int id, ChannelOptions? options = default) => this.AcceptChannel((ulong)id, options);

        /// <summary>
        /// Accepts a channel with a specific ID.
        /// </summary>
        /// <param name="id">The <see cref="Channel.Id"/> of the channel to accept.</param>
        /// <param name="options">A set of options that describe local treatment of this channel.</param>
        /// <returns>The accepted <see cref="Channel"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the channel is already accepted or is no longer offered by the remote party.</exception>
        /// <remarks>
        /// This method can be used to accept anonymous channels created with <see cref="CreateChannel"/>.
        /// Unlike <see cref="AcceptChannelAsync(string, ChannelOptions, CancellationToken)"/> which will await
        /// for a channel offer if a matching one has not been made yet, this method only accepts an offer
        /// for a channel that has already been made.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when no channel with the specified <paramref name="id"/> exists.</exception>
#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters
        public Channel AcceptChannel(ulong id, ChannelOptions? options = default)
#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters
        {
            options = options ?? DefaultChannelOptions;
            Channel? channel;
            lock (this.syncObject)
            {
                if (this.openChannels.TryGetValue(new QualifiedChannelId(id, ChannelSource.Remote), out channel))
                {
                    if (channel.Name is object && this.channelsOfferedByThemByName.TryGetValue(channel.Name, out var queue))
                    {
                        queue.RemoveMidQueue(channel);
                    }
                }
                else if (!this.openChannels.TryGetValue(new QualifiedChannelId(id, ChannelSource.Seeded), out channel))
                {
                    throw new InvalidOperationException(Strings.NoChannelFoundById);
                }
            }

            this.AcceptChannelOrThrow(channel, options);
            return channel;
        }

        /// <inheritdoc cref="RejectChannel(ulong)"/>
        public void RejectChannel(int id) => this.RejectChannel((ulong)id);

        /// <summary>
        /// Rejects an offer for the channel with a specified ID.
        /// </summary>
        /// <param name="id">The ID of the channel whose offer should be rejected.</param>
        /// <exception cref="InvalidOperationException">Thrown if the channel was already accepted.</exception>
        public void RejectChannel(ulong id)
        {
            Channel? channel;
            lock (this.syncObject)
            {
                if (this.openChannels.TryGetValue(new QualifiedChannelId(id, ChannelSource.Remote), out channel))
                {
                    if (channel.Name != null && this.channelsOfferedByThemByName.TryGetValue(channel.Name, out var queue))
                    {
                        queue.RemoveMidQueue(channel);
                    }
                }
                else if (this.openChannels.ContainsKey(new QualifiedChannelId(id, ChannelSource.Seeded)))
                {
                    throw new InvalidOperationException(Strings.NotAllowedOnSeededChannel);
                }
                else
                {
                    throw new InvalidOperationException(Strings.NoChannelFoundById);
                }
            }

            channel.Dispose();
        }

        /// <summary>
        /// Offers a new, named channel to the remote party so they may accept it with <see cref="AcceptChannelAsync(string, ChannelOptions, CancellationToken)"/>.
        /// </summary>
        /// <param name="name">
        /// A name for the channel, which must be accepted on the remote end to complete creation.
        /// It need not be unique, and may be empty but must not be null.
        /// Any characters are allowed, and max length is determined by the maximum frame payload (based on UTF-8 encoding).
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A task that completes with the <see cref="Channel"/> if the offer is accepted on the remote end
        /// or faults with <see cref="MultiplexingProtocolException"/> if the remote end rejects the channel.
        /// </returns>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled before the channel is accepted by the remote end.</exception>
        public Task<Channel> OfferChannelAsync(string name, CancellationToken cancellationToken) => this.OfferChannelAsync(name, options: null, cancellationToken);

        /// <summary>
        /// Offers a new, named channel to the remote party so they may accept it with <see cref="AcceptChannelAsync(string, ChannelOptions, CancellationToken)"/>.
        /// </summary>
        /// <param name="name">
        /// A name for the channel, which must be accepted on the remote end to complete creation.
        /// It need not be unique, and may be empty but must not be null.
        /// Any characters are allowed, and max length is determined by the maximum frame payload (based on UTF-8 encoding).
        /// </param>
        /// <param name="options">A set of options that describe local treatment of this channel.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A task that completes with the <see cref="Channel"/> if the offer is accepted on the remote end
        /// or faults with <see cref="MultiplexingProtocolException"/> if the remote end rejects the channel.
        /// </returns>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled before the channel is accepted by the remote end.</exception>
        public async Task<Channel> OfferChannelAsync(string name, ChannelOptions? options = default, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(name, nameof(name));

            cancellationToken.ThrowIfCancellationRequested();
            Verify.NotDisposed(this);

            var offerParameters = new Channel.OfferParameters(name, options?.ChannelReceivingWindowSize ?? this.DefaultChannelReceivingWindowSize);
            var payload = this.formatter.Serialize(offerParameters);
            var qualifiedId = new QualifiedChannelId(this.GetUnusedChannelId(), ChannelSource.Local);
            Channel channel = new Channel(this, qualifiedId, offerParameters, options ?? DefaultChannelOptions);
            lock (this.syncObject)
            {
                this.openChannels.Add(qualifiedId, channel);
            }

            var header = new FrameHeader
            {
                Code = ControlCode.Offer,
                ChannelId = qualifiedId,
            };

            using (cancellationToken.Register(this.OfferChannelCanceled!, channel))
            {
                await this.SendFrameAsync(header, payload, cancellationToken).ConfigureAwait(false);
                await channel.Acceptance.ConfigureAwait(false);
                return channel;
            }
        }

        /// <summary>
        /// Accepts a channel that the remote end has attempted or may attempt to create.
        /// </summary>
        /// <param name="name">The name of the channel to accept.</param>
        /// <param name="cancellationToken">A token to indicate lost interest in accepting the channel.</param>
        /// <returns>The <see cref="Channel"/>, after its offer has been received from the remote party and accepted.</returns>
        /// <remarks>
        /// If multiple offers exist with the specified <paramref name="name"/>, the first one received will be accepted.
        /// </remarks>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled before a request to create the channel has been received.</exception>
        public Task<Channel> AcceptChannelAsync(string name, CancellationToken cancellationToken) => this.AcceptChannelAsync(name, options: null, cancellationToken);

        /// <summary>
        /// Accepts a channel that the remote end has attempted or may attempt to create.
        /// </summary>
        /// <param name="name">The name of the channel to accept.</param>
        /// <param name="options">A set of options that describe local treatment of this channel.</param>
        /// <param name="cancellationToken">A token to indicate lost interest in accepting the channel.</param>
        /// <returns>The <see cref="Channel"/>, after its offer has been received from the remote party and accepted.</returns>
        /// <remarks>
        /// If multiple offers exist with the specified <paramref name="name"/>, the first one received will be accepted.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown if the channel is already accepted or is no longer offered by the remote party.</exception>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled before a request to create the channel has been received.</exception>
        public async Task<Channel> AcceptChannelAsync(string name, ChannelOptions? options = default, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(name, nameof(name));
            Verify.NotDisposed(this);

            options = options ?? DefaultChannelOptions;

            while (true)
            {
                Channel? channel = null;
                TaskCompletionSource<Channel>? pendingAcceptChannel = null;
                lock (this.syncObject)
                {
                    if (this.channelsOfferedByThemByName.TryGetValue(name, out var channelsOfferedByThem))
                    {
                        while (channel == null && channelsOfferedByThem.Count > 0)
                        {
                            channel = channelsOfferedByThem.Dequeue();
                            if (channel.Acceptance.IsCompleted)
                            {
                                channel = null;
                                continue;
                            }

                            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                            {
                                this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.AcceptChannelAlreadyOffered, "Accepting channel {1} \"{0}\" which is already offered by the other side.", name, channel.QualifiedId);
                            }
                        }
                    }

                    if (channel == null)
                    {
                        if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                        {
                            this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.AcceptChannelWaiting, "Waiting to accept channel \"{0}\", when offered by the other side.", name);
                        }

                        if (!this.acceptingChannels.TryGetValue(name, out var acceptingChannels))
                        {
                            this.acceptingChannels.Add(name, acceptingChannels = new Queue<TaskCompletionSource<Channel>>());
                        }

                        pendingAcceptChannel = new TaskCompletionSource<Channel>(options);
                        acceptingChannels.Enqueue(pendingAcceptChannel);
                    }
                }

                if (channel != null)
                {
                    // In a race condition with the channel offer being canceled, we may fail to accept the channel.
                    // In that case, we'll just loop back around and wait for another one.
                    if (this.TryAcceptChannel(channel, options))
                    {
                        return channel;
                    }
                }
                else
                {
                    using (cancellationToken.Register(this.AcceptChannelCanceled!, Tuple.Create(pendingAcceptChannel, name), false))
                    {
                        channel = await pendingAcceptChannel!.Task.ConfigureAwait(false);

                        // Don't expose the Channel until the thread that is accepting it has applied options.
                        await channel.OptionsApplied.ConfigureAwait(false);
                        return channel;
                    }
                }
            }
        }

        /// <summary>
        /// Immediately closes the underlying transport stream and releases all resources associated with this object and any open channels.
        /// </summary>
        [Obsolete("Use DisposeAsync instead.")]
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
        public void Dispose() => this.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

        /// <summary>
        /// Disposes this multiplexing stream.
        /// </summary>
        /// <returns>A task that indicates when disposal is complete.</returns>
        public async ValueTask DisposeAsync()
        {
            this.disposalTokenSource.Cancel();
            this.completionSource.TrySetResult(null);
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.StreamDisposed, "Disposing.");
            }

            await this.formatter.DisposeAsync().ConfigureAwait(false);

            lock (this.syncObject)
            {
                foreach (var entry in this.openChannels)
                {
                    entry.Value.Dispose();
                }

                foreach (var entry in this.acceptingChannels)
                {
                    foreach (var tcs in entry.Value)
                    {
                        tcs.TrySetCanceled();
                    }
                }

                this.openChannels.Clear();
                this.channelsOfferedByThemByName.Clear();
                this.acceptingChannels.Clear();
            }
        }

        /// <summary>
        /// Raises the <see cref="ChannelOffered"/> event.
        /// </summary>
        /// <param name="args">The arguments to pass to the event handlers.</param>
        protected virtual void OnChannelOffered(ChannelOfferEventArgs args) => this.ChannelOffered?.Invoke(this, args);

        /// <summary>
        /// Reads to fill a buffer.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="buffer">The buffer to fill.</param>
        /// <param name="throwOnEmpty"><c>true</c> to throw if 0 bytes are read before the stream before the end of stream is encountered; <c>false</c> to simply return <c>false</c> when that happens.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns><c>true</c> if the buffer was filled as required; <c>false</c> if the stream was empty and no bytes were read, if <paramref name="throwOnEmpty"/> is <c>false</c>.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the end of the stream was reached before the buffer was filled (unless <paramref name="throwOnEmpty"/> is false and 0 bytes were read).</exception>
        private static async ValueTask<bool> ReadToFillAsync(Stream stream, Memory<byte> buffer, bool throwOnEmpty, CancellationToken cancellationToken)
        {
            Requires.NotNull(stream, nameof(stream));

            int bytesRead = 0;
            while (bytesRead < buffer.Length)
            {
                int bytesReadJustNow = await stream.ReadAsync(buffer.Slice(bytesRead), cancellationToken).ConfigureAwait(false);
                if (bytesReadJustNow == 0)
                {
                    break;
                }

                bytesRead += bytesReadJustNow;
            }

            if (bytesRead < buffer.Length && (bytesRead > 0 || throwOnEmpty))
            {
                throw new EndOfStreamException();
            }

            return bytesRead == buffer.Length;
        }

        /// <summary>
        /// Reads the specified number of bytes from a stream and discards everything read.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The result of the operation.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the end of the stream was reached before <paramref name="length"/> bytes were read.</exception>
        private static async Task ReadAndDiscardAsync(Stream stream, int length, CancellationToken cancellationToken)
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Min(4096, length));
            try
            {
                int bytesRead = 0;
                while (bytesRead < length)
                {
                    var memory = rented.AsMemory(0, Math.Min(rented.Length, length - bytesRead));
                    int bytesJustRead = await stream.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                    if (bytesJustRead == 0)
                    {
                        throw new EndOfStreamException();
                    }

                    bytesRead += bytesJustRead;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static ReadOnlyMemory<T> AsMemory<T>(ReadOnlySequence<T> sequence, Memory<T> backupBuffer)
        {
            if (sequence.IsSingleSegment)
            {
                return sequence.First;
            }
            else
            {
                sequence.CopyTo(backupBuffer.Span);
                return backupBuffer.Slice(0, (int)sequence.Length);
            }
        }

        private async Task ReadStreamAsync()
        {
            Memory<byte> payloadBuffer = new byte[FramePayloadMaxLength];
            try
            {
                while (!this.Completion.IsCompleted)
                {
                    if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Verbose))
                    {
                        this.TraceSource.TraceEvent(TraceEventType.Verbose, (int)TraceEventId.WaitingForNextFrame, "Waiting for next frame");
                    }

                    var frame = await this.formatter.ReadFrameAsync(this.DisposalToken).ConfigureAwait(false);
                    if (!frame.HasValue)
                    {
                        break;
                    }

                    var header = frame.Value.Header;
                    header.FlipChannelPerspective();
                    if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                    {
                        this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.FrameReceived, "Received {0} frame for channel {1} with {2} bytes of content.", header.Code, header.ChannelId, frame.Value.Payload.Length);
                    }

                    switch (header.Code)
                    {
                        case ControlCode.Offer:
                            this.OnOffer(header.RequiredChannelId, frame.Value.Payload);
                            break;
                        case ControlCode.OfferAccepted:
                            this.OnOfferAccepted(header, frame.Value.Payload);
                            break;
                        case ControlCode.Content:
                            await this.OnContentAsync(header, frame.Value.Payload, this.DisposalToken).ConfigureAwait(false);
                            break;
                        case ControlCode.ContentProcessed:
                            this.OnContentProcessed(header, frame.Value.Payload);
                            break;
                        case ControlCode.ContentWritingCompleted:
                            this.OnContentWritingCompleted(header.RequiredChannelId);
                            break;
                        case ControlCode.ChannelTerminated:
                            await this.OnChannelTerminatedAsync(header.RequiredChannelId).ConfigureAwait(false);
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                // When we unexpectedly hit an end of stream, just close up shop.
            }
            finally
            {
                lock (this.syncObject)
                {
                    foreach (var entry in this.openChannels)
                    {
                        entry.Value.OnContentWritingCompleted();
                    }
                }
            }

            await this.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Occurs when the remote party has terminated a channel (including canceling an offer).
        /// </summary>
        /// <param name="channelId">The ID of the terminated channel.</param>
        private async Task OnChannelTerminatedAsync(QualifiedChannelId channelId)
        {
            Channel? channel;
            lock (this.syncObject)
            {
                if (this.openChannels.TryGetValue(channelId, out channel))
                {
                    this.openChannels.Remove(channelId);
                    this.channelsPendingTermination.Remove(channelId);
                    if (channel.Name != null)
                    {
                        if (this.channelsOfferedByThemByName.TryGetValue(channel.Name, out var queue))
                        {
                            queue.RemoveMidQueue(channel);
                        }
                    }
                }
            }

            if (channel is Channel)
            {
                await channel.OnChannelTerminatedAsync().ConfigureAwait(false);
                channel.IsRemotelyTerminated = true;
                channel.Dispose();
            }
        }

        private void OnContentWritingCompleted(QualifiedChannelId channelId)
        {
            Channel channel;
            lock (this.syncObject)
            {
                channel = this.openChannels[channelId];
            }

            if (channelId.Source == ChannelSource.Local && !channel.IsAccepted)
            {
                throw new MultiplexingProtocolException($"Remote party indicated they're done writing to channel {channelId} before accepting it.");
            }

            channel.OnContentWritingCompleted();
        }

        private async ValueTask OnContentAsync(FrameHeader header, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
        {
            Channel channel;
            var channelId = header.RequiredChannelId;
            lock (this.syncObject)
            {
                channel = this.openChannels[channelId];
            }

            try
            {
                if (channelId.Source == ChannelSource.Local && !channel.IsAccepted)
                {
                    throw new MultiplexingProtocolException($"Remote party sent content for channel {channelId} before accepting it.");
                }

                if (!payload.IsEmpty)
                {
                    await channel.OnContentAsync(header, payload, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException) when (channel.IsDisposed)
            {
            }
        }

        private void OnOfferAccepted(FrameHeader header, ReadOnlySequence<byte> payloadBuffer)
        {
            Channel.AcceptanceParameters acceptanceParameters = this.formatter.DeserializeAcceptanceParameters(payloadBuffer);
            var channelId = header.RequiredChannelId;
            Channel? channel;
            lock (this.syncObject)
            {
                if (!this.openChannels.TryGetValue(channelId, out channel))
                {
                    throw new MultiplexingProtocolException("Offer accepted for unknown or forgotten channel ID " + channelId);
                }
            }

            if (!channel.OnAccepted(acceptanceParameters))
            {
                // This may be an acceptance of a channel that we canceled an offer for, and a race condition
                // led to our cancellation notification crossing in transit with their acceptance notification.
                // In this case, do nothing since we already sent a channel termination message, and the remote side
                // should notice it soon.
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Warning))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Warning, (int)TraceEventId.UnexpectedChannelAccept, "Ignoring " + nameof(ControlCode.OfferAccepted) + " message for channel {0} that we already canceled our offer for.", channel.QualifiedId);
                }
            }
        }

        private void OnContentProcessed(FrameHeader header, ReadOnlySequence<byte> payloadBuffer)
        {
            Channel? channel;
            lock (this.syncObject)
            {
                this.openChannels.TryGetValue(header.RequiredChannelId, out channel);
            }

            if (channel is null)
            {
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Warning))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Warning, (int)TraceEventId.UnexpectedContentProcessed, "Ignoring " + nameof(ControlCode.ContentProcessed) + " message for channel {0} that does not exist.", header.ChannelId);
                }

                return;
            }

            long bytesProcessed = this.formatter.DeserializeContentProcessed(payloadBuffer);
            channel.OnContentProcessed(bytesProcessed);
        }

        private void OnOffer(QualifiedChannelId channelId, ReadOnlySequence<byte> payloadBuffer)
        {
            var offerParameters = this.formatter.DeserializeOfferParameters(payloadBuffer);

            var channel = new Channel(this, channelId, offerParameters);
            bool acceptingChannelAlreadyPresent = false;
            ChannelOptions? options = DefaultChannelOptions;
            lock (this.syncObject)
            {
                if (this.acceptingChannels.TryGetValue(offerParameters.Name, out var acceptingChannels))
                {
                    while (acceptingChannels.Count > 0)
                    {
                        var candidate = acceptingChannels.Dequeue();
                        if (candidate.TrySetResult(channel))
                        {
                            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                            {
                                this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelOfferReceived, "Remote party offers channel {1} \"{0}\" which matches up with a pending " + nameof(this.AcceptChannelAsync), offerParameters.Name, channelId);
                            }

                            acceptingChannelAlreadyPresent = true;
                            options = (ChannelOptions?)candidate.Task.AsyncState;
                            Assumes.NotNull(options);
                            break;
                        }
                    }
                }

                if (!acceptingChannelAlreadyPresent)
                {
                    if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                    {
                        this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelOfferReceived, "Remote party offers channel {1} \"{0}\" which has no pending " + nameof(this.AcceptChannelAsync), offerParameters.Name, channelId);
                    }

                    if (!this.channelsOfferedByThemByName.TryGetValue(offerParameters.Name, out var offeredChannels))
                    {
                        this.channelsOfferedByThemByName.Add(offerParameters.Name, offeredChannels = new Queue<Channel>());
                    }

                    offeredChannels.Enqueue(channel);
                }

                this.openChannels.Add(channelId, channel);
            }

            if (acceptingChannelAlreadyPresent)
            {
                this.AcceptChannelOrThrow(channel, options);
            }

            var args = new ChannelOfferEventArgs(channelId, channel.Name, acceptingChannelAlreadyPresent);
            this.OnChannelOffered(args);
        }

        private bool TryAcceptChannel(Channel channel, ChannelOptions options)
        {
            Requires.NotNull(channel, nameof(channel));
            Requires.NotNull(options, nameof(options));

            if (channel.TryAcceptOffer(options))
            {
                return true;
            }

            return false;
        }

        private void AcceptChannelOrThrow(Channel channel, ChannelOptions options)
        {
            Requires.NotNull(channel, nameof(channel));
            Requires.NotNull(options, nameof(options));

            if (!this.TryAcceptChannel(channel, options))
            {
                if (channel.IsAccepted)
                {
                    throw new InvalidOperationException("Channel is already accepted.");
                }
                else if (channel.IsRejectedOrCanceled)
                {
                    throw new InvalidOperationException("Channel is no longer available for acceptance.");
                }
                else
                {
                    throw new InvalidOperationException("Channel could not be accepted.");
                }
            }
        }

        /// <summary>
        /// Raised when <see cref="Channel.Dispose"/> is called and any local transmission is completed.
        /// </summary>
        /// <param name="channel">The channel that is closing down.</param>
        private void OnChannelDisposed(Channel channel)
        {
            Requires.NotNull(channel, nameof(channel));

            if (!this.Completion.IsCompleted)
            {
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelDisposed, "Local channel {0} \"{1}\" stream disposed.", channel.QualifiedId, channel.Name);
                }

                this.SendFrame(ControlCode.ChannelTerminated, channel.QualifiedId);
            }
        }

        /// <summary>
        /// Indicates that the local end will not be writing any more data to this channel,
        /// leading to the transmission of a <see cref="ControlCode.ContentWritingCompleted"/> frame being sent for this channel.
        /// </summary>
        /// <param name="channel">The channel whose writing has finished.</param>
        private void OnChannelWritingCompleted(Channel channel)
        {
            Requires.NotNull(channel, nameof(channel));
            lock (this.syncObject)
            {
                // Only inform the remote side if this channel has not already been terminated.
                if (!this.channelsPendingTermination.Contains(channel.QualifiedId) && this.openChannels.ContainsKey(channel.QualifiedId))
                {
                    this.SendFrame(ControlCode.ContentWritingCompleted, channel.QualifiedId);
                }
            }
        }

        private void SendFrame(ControlCode code, QualifiedChannelId channelId)
        {
            var header = new FrameHeader
            {
                Code = code,
                ChannelId = channelId,
            };
            this.SendFrame(header, payload: default, CancellationToken.None);
        }

        private void SendFrame(FrameHeader header, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
        {
            if (this.Completion.IsCompleted)
            {
                // Any frames that come in after we're done are most likely frames just informing that channels are being terminated,
                // which we do not need to communicate since the connection going down implies that.
                return;
            }

            this.DisposeSelfOnFailure(this.SendFrameAsync(header, payload: payload, cancellationToken));
        }

        private async Task SendFrameAsync(FrameHeader header, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
        {
            Assumes.True(payload.Length <= FramePayloadMaxLength, nameof(payload), "Frame content exceeds max limit.");
            Verify.NotDisposed(this);

            await this.sendingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var qualifiedChannelId = header.RequiredChannelId;
                lock (this.syncObject)
                {
                    if (header.Code == ControlCode.ChannelTerminated)
                    {
                        if (this.openChannels.ContainsKey(qualifiedChannelId))
                        {
                            // We're sending the first termination message. Record this so we can be sure to NOT
                            // send any more frames regarding this channel.
                            Assumes.True(this.channelsPendingTermination.Add(qualifiedChannelId), "Sending ChannelTerminated more than once for channel {0}.", header.ChannelId);
                        }
                    }
                    else
                    {
                        if (this.channelsPendingTermination.Contains(qualifiedChannelId))
                        {
                            // We shouldn't ever send a frame about a channel after we've transmitted a ChannelTermination frame for it.
                            // But backpressure frames *can* come in 'late' because even after both sides have finished writing, they may still be reading
                            // what they've received.
                            // In such cases, we should just suppress transmission of the frame because the other side does not care.
                            if (header.Code != ControlCode.ContentProcessed)
                            {
                                Assumes.Fail($"Sending {header.Code} frame for channel {header.ChannelId}, which we've already sent termination for.");
                            }
                        }
                    }
                }

                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.FrameSent, "Sending {0} frame for channel {1}, carrying {2} bytes of content.", header.Code, header.ChannelId, (int)payload.Length);
                }

                if (!payload.IsEmpty && this.TraceSource.Switch.ShouldTrace(TraceEventType.Verbose))
                {
                    this.TraceSource.TraceData(TraceEventType.Verbose, (int)TraceEventId.FrameSentPayload, payload);
                }

                this.formatter.WriteFrame(header, payload);
                await this.formatter.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                this.sendingSemaphore.Release();
            }
        }

        /// <summary>
        /// Gets a unique number that can be used to represent a channel.
        /// </summary>
        /// <returns>An unused channel number.</returns>
        /// <remarks>
        /// In protocol major versions 1-2, the channel numbers increase by two in order to maintain odd or even numbers, since each party is allowed to create only one or the other.
        /// </remarks>
        private ulong GetUnusedChannelId() => (ulong)Interlocked.Add(ref this.lastOfferedChannelId, this.isOdd.HasValue ? 2 : 1);

        private void OfferChannelCanceled(object state)
        {
            Requires.NotNull(state, nameof(state));
            var channel = (Channel)state;
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.OfferChannelCanceled, "Offer of channel {1} (\"{0}\") canceled.", channel.Name, channel.QualifiedId);
            }

            channel.Dispose();
        }

        private void AcceptChannelCanceled(object state)
        {
            Requires.NotNull(state, nameof(state));
            var (channelSource, name) = (Tuple<TaskCompletionSource<Channel>, string>)state;
            if (channelSource.TrySetCanceled())
            {
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.AcceptChannelCanceled, "Cancelling " + nameof(this.AcceptChannelAsync) + " for \"{0}\"", name);
                }

                lock (this.syncObject)
                {
                    if (this.acceptingChannels.TryGetValue(name, out var queue))
                    {
                        Assumes.True(queue.RemoveMidQueue(channelSource));
                    }
                }
            }
            else
            {
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.AcceptChannelCanceled, "Cancelling " + nameof(this.AcceptChannelAsync) + " for \"{0}\" attempted but failed.", name);
                }
            }
        }

        private void DisposeSelfOnFailure(Task task)
        {
            Requires.NotNull(task, nameof(task));

            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    this.Fault(task.Exception!.InnerException ?? task.Exception);
                }
            }
            else
            {
                task.ContinueWith(
                    (t, s) => ((MultiplexingStream)s!).Fault(t.Exception!.InnerException ?? t.Exception),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default).Forget();
            }
        }

        private void Fault(Exception exception)
        {
            if (exception is ObjectDisposedException && this.Completion.IsCompleted)
            {
                // We're already disposed. Nothing more to do.
                return;
            }

            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Critical))
            {
                this.TraceSource.TraceEvent(TraceEventType.Critical, (int)TraceEventId.FatalError, "Disposing self due to exception: {0}", exception);
            }

            this.completionSource.TrySetException(exception);
            this.DisposeAsync().Forget();
        }
    }
}
