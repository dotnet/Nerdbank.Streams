// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

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
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// Encodes multiple channels over a single transport.
    /// </summary>
    public partial class MultiplexingStream : IDisposableObservable
    {
        /// <summary>
        /// The channel id reserved for control frames.
        /// </summary>
        private const int ControlChannelId = 0;

        /// <summary>
        /// The magic number to send at the start of communication.
        /// </summary>
        /// <remarks>
        /// If the protocol ever changes, change this random number. It serves both as a way to recognize the other end actually supports multiplexing and ensure compatibility.
        /// </remarks>
        private static readonly byte[] ProtocolMagicNumber = BitConverter.GetBytes(420282205);

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
        /// The maximum length of a frame's payload.
        /// </summary>
        private readonly int framePayloadMaxLength = 20 * 1024;

        /// <summary>
        /// A value indicating whether this is the "odd" party in the conversation (where the other one would be "even").
        /// </summary>
        /// <remarks>
        /// This value is only significant for parts of the protocol where it's useful to have the two parties behave slightly differently to avoid conflicts.
        /// </remarks>
        private readonly bool isOdd;

        /// <summary>
        /// The underlying transport.
        /// </summary>
        private readonly Stream stream;

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
        /// A dictionary of all channels, keyed by their number.
        /// </summary>
        private readonly Dictionary<int, Channel> openChannels = new Dictionary<int, Channel>();

        /// <summary>
        /// A semaphore that must be entered to write to the underlying transport <see cref="stream"/>.
        /// </summary>
        private readonly SemaphoreSlim sendingSemaphore = new SemaphoreSlim(1);

        /// <summary>
        /// The source for the <see cref="Completion"/> property.
        /// </summary>
        private readonly TaskCompletionSource<object> completionSource = new TaskCompletionSource<object>();

        /// <summary>
        /// A buffer used only by <see cref="SendFrameAsync(FrameHeader, ReadOnlySequence{byte}, CancellationToken)"/>.
        /// </summary>
        private readonly Memory<byte> sendingHeaderBuffer = new byte[FrameHeader.HeaderLength];

        /// <summary>
        /// A token that is canceled when this instance is disposed.
        /// </summary>
        private readonly CancellationTokenSource disposalTokenSource = new CancellationTokenSource();

        /// <summary>
        /// The last number assigned to a channel.
        /// Each use of this should increment by two.
        /// </summary>
        private int lastOfferedChannelId;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class.
        /// </summary>
        /// <param name="stream">The stream to multiplex multiple channels over.</param>
        /// <param name="isOdd">A value indicating whether this party is the "odd" one.</param>
        /// <param name="options">The options for this instance.</param>
        private MultiplexingStream(Stream stream, bool isOdd, Options options)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.NotNull(options, nameof(options));

            this.stream = stream;
            this.isOdd = isOdd;
            this.lastOfferedChannelId = isOdd ? -1 : 0; // the first channel created should be 1 or 2
            this.TraceSource = options.TraceSource;

            // Initiate reading from the transport stream. This will not end until the stream does, or we're disposed.
            // If reading the stream fails, we'll dispose ourselves.
            this.DisposeSelfOnFailure(this.ReadStreamAsync());
        }

        /// <summary>
        /// Occurs when the remote party offers to establish a channel.
        /// </summary>
        public event EventHandler<ChannelOfferEventArgs> ChannelOffered;

        private enum TraceEventId
        {
            MessageReceivedForUnknownChannel = 1,
            HandshakeSuccessful = 2,
            UnexpectedEndOfStream = 3,
            HandshakeFailed = 4,
            FatalError = 5,
            UnexpectedChannelAccept = 6,
            ChannelAutoClosing = 7,
            StreamDisposed = 8,
            AcceptChannelWaiting = 9,
            AcceptChannelAlreadyOffered = 10,
            AcceptChannelCanceled = 11,
            ChannelOfferReceived = 12,
            ChannelDisposed = 13,
            OfferChannelCanceled = 14,
            FrameSent = 15,
            FrameReceived = 16,
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

        /// <inheritdoc />
        bool IDisposableObservable.IsDisposed => this.Completion.IsCompleted;

        /// <summary>
        /// Gets a token that is canceled when this instance is disposed.
        /// </summary>
        internal CancellationToken DisposalToken => this.disposalTokenSource.Token;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class.
        /// </summary>
        /// <param name="stream">The stream to multiplex multiple channels over.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The multiplexing stream, once the handshake is complete.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the remote end disconnects before the handshake is complete.</exception>
        public static Task<MultiplexingStream> CreateAsync(Stream stream, CancellationToken cancellationToken = default) => CreateAsync(stream, options: null, cancellationToken);

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class.
        /// </summary>
        /// <param name="stream">The stream to multiplex multiple channels over.</param>
        /// <param name="options">Options to define behavior for the multiplexing stream.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The multiplexing stream, once the handshake is complete.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the remote end disconnects before the handshake is complete.</exception>
        public static async Task<MultiplexingStream> CreateAsync(Stream stream, Options options, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanRead, nameof(stream), "Stream must be readable.");
            Requires.Argument(stream.CanWrite, nameof(stream), "Stream must be writable.");

            options = options ?? new Options();

            // Send the protocol magic number, and a random GUID to establish even/odd assignments.
            var randomSendBuffer = Guid.NewGuid().ToByteArray();
            var sendBuffer = new byte[ProtocolMagicNumber.Length + randomSendBuffer.Length];
            Array.Copy(ProtocolMagicNumber, sendBuffer, ProtocolMagicNumber.Length);
            Array.Copy(randomSendBuffer, 0, sendBuffer, ProtocolMagicNumber.Length, randomSendBuffer.Length);
            Task writeTask = stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);
            Task flushTask = stream.FlushAsync(cancellationToken);

            var recvBuffer = new byte[sendBuffer.Length];
            await ReadToFillAsync(stream, recvBuffer, throwOnEmpty: true, cancellationToken).ConfigureAwait(false);

            // Realize any exceptions from writing to the stream.
            await Task.WhenAll(writeTask, flushTask).ConfigureAwait(false);

            for (int i = 0; i < ProtocolMagicNumber.Length; i++)
            {
                if (recvBuffer[i] != ProtocolMagicNumber[i])
                {
                    string message = "Protocol handshake mismatch.";
                    if (options.TraceSource.Switch.ShouldTrace(TraceEventType.Critical))
                    {
                        options.TraceSource.TraceEvent(TraceEventType.Critical, (int)TraceEventId.HandshakeFailed, message);
                    }

                    throw new MultiplexingProtocolException(message);
                }
            }

            bool? isOdd = null;
            for (int i = 0; i < randomSendBuffer.Length; i++)
            {
                byte sent = randomSendBuffer[i];
                byte recv = recvBuffer[ProtocolMagicNumber.Length + i];
                if (sent > recv)
                {
                    isOdd = true;
                    break;
                }
                else if (sent < recv)
                {
                    isOdd = false;
                    break;
                }
            }

            if (!isOdd.HasValue)
            {
                string message = "Unable to determine even/odd party.";
                if (options.TraceSource.Switch.ShouldTrace(TraceEventType.Critical))
                {
                    options.TraceSource.TraceEvent(TraceEventType.Critical, (int)TraceEventId.HandshakeFailed, message);
                }

                throw new MultiplexingProtocolException(message);
            }

            if (options.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                options.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.HandshakeSuccessful, "Multiplexing protocol established successfully.");
            }

            return new MultiplexingStream(stream, isOdd.Value, options);
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
        public Channel CreateChannel(ChannelOptions options = default)
        {
            Channel channel = new Channel(this, this.GetUnusedChannelId(), string.Empty, offeredByRemote: false, options ?? DefaultChannelOptions);
            lock (this.syncObject)
            {
                this.openChannels.Add(channel.Id, channel);
            }

            this.SendFrame(ControlCode.Offer, channel.Id);
            return channel;
        }

        /// <summary>
        /// Accepts a channel with a specific ID.
        /// </summary>
        /// <param name="id">The <see cref="Channel.Id"/> of the channel to accept.</param>
        /// <param name="options">A set of options that describe local treatment of this channel.</param>
        /// <returns>The accepted <see cref="Channel"/>.</returns>
        /// <remarks>
        /// This method can be used to accept anonymous channels created with <see cref="CreateChannel"/>.
        /// Unlike <see cref="AcceptChannelAsync(string, ChannelOptions, CancellationToken)"/> which will await
        /// for a channel offer if a matching one has not been made yet, this method only accepts an offer
        /// for a channel that has already been made.
        /// </remarks>
        public Channel AcceptChannel(int id, ChannelOptions options = default)
        {
            Channel channel;
            lock (this.syncObject)
            {
                Verify.Operation(this.openChannels.TryGetValue(id, out channel), "No channel with that ID found.");
                if (channel.Name != null && this.channelsOfferedByThemByName.TryGetValue(channel.Name, out var queue))
                {
                    queue.RemoveMidQueue(channel);
                }
            }

            this.AcceptChannelOrThrow(channel, options);
            return channel;
        }

        /// <summary>
        /// Rejects an offer for the channel with a specified ID.
        /// </summary>
        /// <param name="id">The ID of the channel whose offer should be rejected.</param>
        /// <exception cref="InvalidOperationException">Thrown if the channel was already accepted.</exception>
        public void RejectChannel(int id)
        {
            Channel channel;
            lock (this.syncObject)
            {
                Verify.Operation(this.openChannels.TryGetValue(id, out channel), "No channel with that ID found.");
                if (channel.Name != null && this.channelsOfferedByThemByName.TryGetValue(channel.Name, out var queue))
                {
                    queue.RemoveMidQueue(channel);
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
        public async Task<Channel> OfferChannelAsync(string name, ChannelOptions options = default, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(name, nameof(name));

            cancellationToken.ThrowIfCancellationRequested();
            Memory<byte> payload = ControlFrameEncoding.GetBytes(name);
            Requires.Argument(payload.Length <= this.framePayloadMaxLength, nameof(name), "{0} encoding of value exceeds maximum frame payload length.", ControlFrameEncoding.EncodingName);
            Channel channel = new Channel(this, this.GetUnusedChannelId(), name, offeredByRemote: false, options ?? DefaultChannelOptions);
            lock (this.syncObject)
            {
                this.openChannels.Add(channel.Id, channel);
            }

            var header = new FrameHeader
            {
                Code = ControlCode.Offer,
                FramePayloadLength = payload.Length,
                ChannelId = channel.Id,
            };

            using (cancellationToken.Register(this.OfferChannelCanceled, channel))
            {
                await this.SendFrameAsync(header, new ReadOnlySequence<byte>(payload), cancellationToken).ConfigureAwait(false);
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
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled before a request to create the channel has been received.</exception>
        public async Task<Channel> AcceptChannelAsync(string name, ChannelOptions options = default, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(name, nameof(name));
            Verify.NotDisposed(this);

            Channel channel = null;
            TaskCompletionSource<Channel> pendingAcceptChannel = null;
            lock (this.syncObject)
            {
                if (this.channelsOfferedByThemByName.TryGetValue(name, out var channelsOfferedByThem))
                {
                    while (channelsOfferedByThem.Count > 0)
                    {
                        channel = channelsOfferedByThem.Dequeue();
                        if (channel.Acceptance.IsCompleted)
                        {
                            channel = null;
                            continue;
                        }

                        if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                        {
                            this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.AcceptChannelAlreadyOffered, "Accepting channel {1} \"{0}\" which is already offered by the other side.", name, channel.Id);
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
                this.AcceptChannelOrThrow(channel, options);
                return channel;
            }
            else
            {
                using (cancellationToken.Register(this.AcceptChannelCanceled, Tuple.Create(pendingAcceptChannel, name), false))
                {
                    return await pendingAcceptChannel.Task.ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Immediately closes the underlying transport stream and releases all resources associated with this object and any open channels.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> if we should dispose managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.disposalTokenSource.Cancel();
                this.completionSource.TrySetResult(null);
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.StreamDisposed, "Disposing.");
                }

                this.stream.Dispose();
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

        private static unsafe string DecodeString(ReadOnlyMemory<byte> buffer)
        {
            using (var pinnedBuffer = buffer.Pin())
            {
                return ControlFrameEncoding.GetString((byte*)pinnedBuffer.Pointer, buffer.Length);
            }
        }

        private async Task ReadStreamAsync()
        {
            Memory<byte> buffer = new byte[FrameHeader.HeaderLength + this.framePayloadMaxLength];
            Memory<byte> headerBuffer = buffer.Slice(0, FrameHeader.HeaderLength);
            Memory<byte> payloadBuffer = buffer.Slice(FrameHeader.HeaderLength);
            while (!this.Completion.IsCompleted)
            {
                if (!await ReadToFillAsync(this.stream, headerBuffer, throwOnEmpty: false, this.DisposalToken).ConfigureAwait(false))
                {
                    break;
                }

                var header = FrameHeader.Deserialize(headerBuffer.Span);
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.FrameReceived, "Received {0} frame for channel {1} with {2} bytes of content.", header.Code, header.ChannelId, header.FramePayloadLength);
                }

                switch (header.Code)
                {
                    case ControlCode.Offer:
                        await this.OnOffer(header.ChannelId, payloadBuffer.Slice(0, header.FramePayloadLength), this.DisposalToken);
                        break;
                    case ControlCode.OfferAccepted:
                        this.OnOfferAccepted(header.ChannelId);
                        break;
                    case ControlCode.Content:
                        await this.OnContent(header, this.DisposalToken);
                        break;
                    case ControlCode.ContentWritingCompleted:
                        this.OnContentWritingCompleted(header.ChannelId);
                        break;
                    case ControlCode.ChannelTerminated:
                        this.OnChannelTerminated(header.ChannelId);
                        break;
                    default:
                        break;
                }
            }

            this.Dispose();
        }

        /// <summary>
        /// Occurs when the remote party has terminated a channel (including canceling an offer).
        /// </summary>
        /// <param name="channelId">The ID of the terminated channel.</param>
        private void OnChannelTerminated(int channelId)
        {
            Channel channel;
            lock (this.syncObject)
            {
                if (this.openChannels.TryGetValue(channelId, out channel))
                {
                    this.openChannels.Remove(channelId);
                    if (channel.Name != null)
                    {
                        if (this.channelsOfferedByThemByName.TryGetValue(channel.Name, out var queue))
                        {
                            queue.RemoveMidQueue(channel);
                        }
                    }
                }
            }

            channel?.Dispose();
        }

        private void OnContentWritingCompleted(int channelId)
        {
            Channel channel;
            lock (this.syncObject)
            {
                channel = this.openChannels[channelId];
            }

            channel.ReceivedMessagePipeWriter.Complete();
        }

        private async ValueTask<FlushResult> OnContent(FrameHeader header, CancellationToken cancellationToken)
        {
            Channel channel;
            lock (this.syncObject)
            {
                channel = this.openChannels[header.ChannelId];
            }

            // Read directly from the transport stream to memory that the targeted channel's reader will read from for 0 extra buffer copies.
            PipeWriter writer = channel.ReceivedMessagePipeWriter;
            Memory<byte> memory = writer.GetMemory(header.FramePayloadLength);
            await ReadToFillAsync(this.stream, memory.Slice(0, header.FramePayloadLength), throwOnEmpty: true, cancellationToken);
            writer.Advance(header.FramePayloadLength);
            return await writer.FlushAsync(cancellationToken);
        }

        private void OnOfferAccepted(int channelId)
        {
            Channel channel;
            lock (this.syncObject)
            {
                if (!this.openChannels.TryGetValue(channelId, out channel))
                {
                    throw new MultiplexingProtocolException("Unexpected channel created with ID " + channelId);
                }
            }

            if (!channel.OnAccepted())
            {
                // This may be an acceptance of a channel that we canceled an offer for, and a race condition
                // led to our cancellation notification crossing in transit with their acceptance notification.
                // In this case, do nothing since we already sent a channel termination message, and the remote side
                // should notice it soon.
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Warning))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Warning, (int)TraceEventId.UnexpectedChannelAccept, "Ignoring " + nameof(ControlCode.OfferAccepted) + " message for channel {0} that we already canceled our offer for.", channel.Id);
                }
            }
        }

        private async ValueTask OnOffer(int channelId, Memory<byte> payloadBuffer, CancellationToken cancellationToken)
        {
            await ReadToFillAsync(this.stream, payloadBuffer, throwOnEmpty: true, cancellationToken);
            string name = DecodeString(payloadBuffer);

            var channel = new Channel(this, channelId, name, offeredByRemote: true, channelOptions: DefaultChannelOptions);
            bool acceptingChannelAlreadyPresent = false;
            ChannelOptions options = null;
            lock (this.syncObject)
            {
                if (name != null && this.acceptingChannels.TryGetValue(name, out var acceptingChannels))
                {
                    while (acceptingChannels.Count > 0)
                    {
                        var candidate = acceptingChannels.Dequeue();
                        if (candidate.TrySetResult(channel))
                        {
                            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                            {
                                this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelOfferReceived, "Remote party offers channel {1} \"{0}\" which matches up with a pending " + nameof(this.AcceptChannelAsync), name, channelId);
                            }

                            acceptingChannelAlreadyPresent = true;
                            options = (ChannelOptions)candidate.Task.AsyncState;
                            break;
                        }
                    }
                }

                if (!acceptingChannelAlreadyPresent)
                {
                    if (name != null)
                    {
                        if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                        {
                            this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelOfferReceived, "Remote party offers channel {1} \"{0}\" which has no pending " + nameof(this.AcceptChannelAsync), name, channelId);
                        }

                        if (!this.channelsOfferedByThemByName.TryGetValue(name, out var offeredChannels))
                        {
                            this.channelsOfferedByThemByName.Add(name, offeredChannels = new Queue<Channel>());
                        }

                        offeredChannels.Enqueue(channel);
                    }
                    else
                    {
                        if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                        {
                            this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelOfferReceived, "Remote party offers anonymous channel {0}", channelId);
                        }
                    }
                }

                this.openChannels.Add(channelId, channel);
            }

            if (acceptingChannelAlreadyPresent)
            {
                this.AcceptChannelOrThrow(channel, options);
            }

            var args = new ChannelOfferEventArgs(channel.Id, channel.Name, acceptingChannelAlreadyPresent);
            this.OnChannelOffered(args);
        }

        private void AcceptChannelOrThrow(Channel channel, ChannelOptions options)
        {
            Requires.NotNull(channel, nameof(channel));

            if (channel.TryAcceptOffer(options))
            {
                this.SendFrame(ControlCode.OfferAccepted, channel.Id);
            }
            else if (channel.IsAccepted)
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

        private void OnChannelDisposed(Channel channel)
        {
            Requires.NotNull(channel, nameof(channel));

            if (!this.Completion.IsCompleted)
            {
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelDisposed, "Local channel \"{0}\" stream disposed.", channel.Name);
                }

                this.SendFrame(ControlCode.ChannelTerminated, channel.Id);
            }
        }

        private void OnChannelWritingCompleted(Channel channel)
        {
            Requires.NotNull(channel, nameof(channel));
            lock (this.syncObject)
            {
                // Only inform the remote side if this channel has not already been terminated.
                if (!channel.IsDisposed && this.openChannels.ContainsKey(channel.Id))
                {
                    this.SendFrame(ControlCode.ContentWritingCompleted, channel.Id);
                }
            }
        }

        private void SendFrame(ControlCode code, int channelId)
        {
            if (this.Completion.IsCompleted)
            {
                // Any frames that come in after we're done are most likely frames just informing that channels are being terminated,
                // which we do not need to communicate since the connection going down implies that.
                return;
            }

            var header = new FrameHeader
            {
                Code = code,
                ChannelId = channelId,
                FramePayloadLength = 0,
            };
            this.DisposeSelfOnFailure(this.SendFrameAsync(header, payload: default, CancellationToken.None));
        }

        private async Task FlushAsync(CancellationToken cancellationToken)
        {
            await this.sendingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                this.sendingSemaphore.Release();
            }
        }

        private async Task SendFrameAsync(FrameHeader header, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
        {
            Assumes.True(payload.Length <= this.framePayloadMaxLength, nameof(payload), "Frame content exceeds max limit.");
            Verify.NotDisposed(this);

            Task flushTask;
            await this.sendingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.FrameSent, "Sending {0} frame for channel {1}, carrying {2} bytes of content.", header.Code, header.ChannelId, (int)payload.Length);
                }

                header.Serialize(this.sendingHeaderBuffer.Span);

                // Don't propagate the CancellationToken any more to avoid corrupting the stream with a half-written frame.
                // We're hedging our bets since we don't know whether the Stream will abort a partial write given a canceled token.
                var writeHeaderTask = this.stream.WriteAsync(this.sendingHeaderBuffer, CancellationToken.None);
                if (!payload.IsEmpty)
                {
                    if (payload.IsSingleSegment)
                    {
                        await this.stream.WriteAsync(payload.First).ConfigureAwait(false);
                    }
                    else
                    {
                        // Perf consideration: would it be better to WriteAsync all and await the resulting Tasks together later?
                        foreach (ReadOnlyMemory<byte> segment in payload)
                        {
                            await this.stream.WriteAsync(segment).ConfigureAwait(false);
                        }
                    }
                }

                await writeHeaderTask.ConfigureAwait(false); // rethrow any exception
                flushTask = this.stream.FlushAsync(CancellationToken.None);
            }
            finally
            {
                this.sendingSemaphore.Release();
            }

            await flushTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a unique number that can be used to represent a channel.
        /// </summary>
        /// <returns>An unused channel number.</returns>
        /// <remarks>
        /// The channel numbers increase by two in order to maintain odd or even numbers, since each party is allowed to create only one or the other.
        /// </remarks>
        private int GetUnusedChannelId() => Interlocked.Add(ref this.lastOfferedChannelId, 2);

        private void OfferChannelCanceled(object state)
        {
            Requires.NotNull(state, nameof(state));
            var channel = (Channel)state;
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.OfferChannelCanceled, "Offer of channel {1} (\"{0}\") canceled.", channel.Name, channel.Id);
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
                    this.Fault(task.Exception.InnerException ?? task.Exception);
                }
            }
            else
            {
                task.ContinueWith(
                    (t, s) => ((MultiplexingStream)s).Fault(t.Exception.InnerException ?? t.Exception),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default).Forget();
            }
        }

        private void Fault(Exception exception)
        {
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Critical))
            {
                this.TraceSource.TraceEvent(TraceEventType.Critical, (int)TraceEventId.FatalError, "Disposing self due to exception: {0}", exception);
            }

            this.completionSource.TrySetException(exception);
            this.Dispose();
        }
    }
}
