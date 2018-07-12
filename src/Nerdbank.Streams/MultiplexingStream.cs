// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
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
    public partial class MultiplexingStream : IDisposable, IDisposableObservable
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
        /// The maximum frame length.
        /// </summary>
        private readonly int maxFrameLength = 20 * 1024;

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
        /// </summary>
        private readonly Dictionary<string, Queue<Channel>> channelsOfferedByThem = new Dictionary<string, Queue<Channel>>(StringComparer.Ordinal);

        /// <summary>
        /// A dictionary of channels being offered by ourselves but not yet accepted by the remote end, keyed by <see cref="Channel.Id"/>.
        /// </summary>
        private readonly Dictionary<int, Channel> channelsOfferedByUs = new Dictionary<int, Channel>();

        /// <summary>
        /// A dictionary of channels being accepted (but not yet offered).
        /// </summary>
        private readonly Dictionary<string, Queue<Channel>> acceptingChannels = new Dictionary<string, Queue<Channel>>(StringComparer.Ordinal);

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
        /// A buffer used only by <see cref="SendFrameAsync(FrameHeader, ArraySegment{byte}, bool, CancellationToken)"/>.
        /// </summary>
        private readonly byte[] sendingHeaderBuffer = new byte[FrameHeader.HeaderLength];

        /// <summary>
        /// The backing field for the <see cref="DefaultChannelPriority"/> property.
        /// </summary>
        private int defaultChannelPriority = 100;

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
        /// <param name="traceSource">The logger to use.</param>
        private MultiplexingStream(Stream stream, bool isOdd, TraceSource traceSource)
        {
            Requires.NotNull(stream, nameof(stream));

            this.stream = stream;
            this.isOdd = isOdd;
            this.lastOfferedChannelId = isOdd ? -1 : 0; // the first channel created should be 1 or 2
#if TRACESOURCE
            this.TraceSource = traceSource;
#endif

            // Initiate reading from the transport stream. This will not end until the stream does, or we're disposed.
            // If reading the stream fails, we'll dispose ourselves.
            this.DisposeSelfOnFailure(this.ReadStreamAsync());
        }

        private enum TraceEventId
        {
            MessageReceivedForUnknownChannel = 1,
        }

        /// <summary>
        /// Gets or sets the priority to use for new channels.
        /// </summary>
        public int DefaultChannelPriority
        {
            get
            {
                Verify.NotDisposed(this);
                return this.defaultChannelPriority;
            }

            set
            {
                Requires.Range(value > 0, nameof(value), "A positive number is required.");
                Verify.NotDisposed(this);
                this.defaultChannelPriority = value;
            }
        }

        /// <summary>
        /// Gets a task that completes when this instance is disposed, and may have captured a fault that led to its self-disposal.
        /// </summary>
        public Task Completion => this.completionSource.Task;

#if TRACESOURCE
        /// <summary>
        /// Gets the logger used by this instance.
        /// </summary>
        /// <value>Never null.</value>
        public TraceSource TraceSource { get; }
#endif

        /// <inheritdoc />
        bool IDisposableObservable.IsDisposed => this.Completion.IsCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class.
        /// </summary>
        /// <param name="stream">The stream to multiplex multiple channels over.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The multiplexing stream, once the handshake is complete.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the remote end disconnects before the handshake is complete.</exception>
        public static Task<MultiplexingStream> CreateAsync(Stream stream, CancellationToken cancellationToken) => CreateAsync(stream, null, cancellationToken);

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingStream"/> class.
        /// </summary>
        /// <param name="stream">The stream to multiplex multiple channels over.</param>
        /// <param name="traceSource">The logger to use. May be null.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The multiplexing stream, once the handshake is complete.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the remote end disconnects before the handshake is complete.</exception>
#if TRACESOURCE
        public
#else
#pragma warning disable SA1202
        private
#endif
        static async Task<MultiplexingStream> CreateAsync(Stream stream, TraceSource traceSource, CancellationToken cancellationToken)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanRead, nameof(stream), "Stream must be readable.");
            Requires.Argument(stream.CanWrite, nameof(stream), "Stream must be writable.");

#if TRACESOURCE
            if (traceSource == null)
            {
                traceSource = new TraceSource(nameof(MultiplexingStream), SourceLevels.Critical);
            }
#endif

            // Send the protocol magic number, and a random GUID to establish even/odd assignments.
            var randomSendBuffer = Guid.NewGuid().ToByteArray();
            var sendBuffer = new byte[ProtocolMagicNumber.Length + randomSendBuffer.Length];
            Array.Copy(ProtocolMagicNumber, sendBuffer, ProtocolMagicNumber.Length);
            Array.Copy(randomSendBuffer, 0, sendBuffer, ProtocolMagicNumber.Length, randomSendBuffer.Length);
            Task writeTask = stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);
            Task flushTask = stream.FlushAsync(cancellationToken);

            var recvBuffer = new byte[sendBuffer.Length];
            int bytesRead = await ReadAtLeastAsync(stream, new ArraySegment<byte>(recvBuffer), recvBuffer.Length).ConfigureAwait(false);
            if (bytesRead < recvBuffer.Length)
            {
                // The remote end hung up.
#if TRACESOURCE
                if (traceSource.Switch.ShouldTrace(TraceEventType.Critical))
                {
                    traceSource.TraceInformation("Stream closed during handshake.");
                }
#endif
                throw new EndOfStreamException();
            }

            // Realize any exceptions from writing to the stream.
            await Task.WhenAll(writeTask, flushTask).ConfigureAwait(false);

            for (int i = 0; i < ProtocolMagicNumber.Length; i++)
            {
                if (recvBuffer[i] != ProtocolMagicNumber[i])
                {
                    string message = "Protocol handshake mismatch.";
#if TRACESOURCE
                    if (traceSource.Switch.ShouldTrace(TraceEventType.Critical))
                    {
                        traceSource.TraceInformation(message);
                    }
#endif
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
#if TRACESOURCE
                if (traceSource.Switch.ShouldTrace(TraceEventType.Critical))
                {
                    traceSource.TraceInformation(message);
                }
#endif
                throw new MultiplexingProtocolException(message);
            }

#if TRACESOURCE
            if (traceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                traceSource.TraceInformation("Multiplexing protocol established successfully.");
            }
#endif
            return new MultiplexingStream(stream, isOdd.Value, traceSource);
        }

        /// <summary>
        /// Creates a new channel.
        /// </summary>
        /// <param name="name">The channel identifier, which must be accepted on the remote end to complete creation.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A task that completes with the <see cref="Stream"/> if the channel is accepted on the remote end
        /// or faults with <see cref="MultiplexingProtocolException"/> if the remote end rejects the channel.
        /// </returns>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled before the channel is accepted by the remote end.</exception>
        public async Task<Stream> CreateChannelAsync(string name, CancellationToken cancellationToken)
        {
            Requires.NotNull(name, nameof(name));

            cancellationToken.ThrowIfCancellationRequested();
            Channel channel = new Channel(this, this.GetUnusedChannelId(), name);
            lock (this.syncObject)
            {
                this.channelsOfferedByUs.Add(channel.Id.Value, channel);
            }

            var header = new FrameHeader
            {
                Code = ControlCode.CreatingChannel,
                FramePayloadLength = ControlFrameEncoding.GetByteCount(name),
                ChannelId = channel.Id.Value,
            };
            byte[] payload = ControlFrameEncoding.GetBytes(name);

            using (cancellationToken.Register(this.CreateChannelCanceled, channel))
            {
                await this.SendFrameAsync(header, new ArraySegment<byte>(payload), flush: true, cancellationToken).ConfigureAwait(false);
                return await channel.Stream.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Accepts a channel that the remote end has attempted or may attempt to create.
        /// </summary>
        /// <param name="name">The identifier of the channel to accept.</param>
        /// <param name="cancellationToken">A token to indicate lost interest in accepting the channel.</param>
        /// <returns>A task whose result is a full-duplex <see cref="Stream"/> when the channel is created by the remote end and accepted by this end.</returns>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled before a request to create the channel has been received.</exception>
        public async Task<Stream> AcceptChannelAsync(string name, CancellationToken cancellationToken)
        {
            Requires.NotNull(name, nameof(name));
            Verify.NotDisposed(this);

            Channel channel;
            bool offerFound;
            lock (this.syncObject)
            {
                if (this.channelsOfferedByThem.TryGetValue(name, out var channelsOfferedByThem) && channelsOfferedByThem.Count > 0)
                {
                    this.TraceInformation("Accepting channel \"{0}\" which is already offered by the other side.", name);
                    channel = channelsOfferedByThem.Dequeue();
                    offerFound = true;
                }
                else
                {
                    this.TraceInformation("Waiting to accept channel \"{0}\", when offered by the other side.", name);
                    if (!this.acceptingChannels.TryGetValue(name, out var acceptingChannels))
                    {
                        this.acceptingChannels.Add(name, acceptingChannels = new Queue<Channel>());
                    }

                    acceptingChannels.Enqueue(channel = new Channel(this, null, name));
                    offerFound = false;
                }
            }

            if (offerFound)
            {
                return channel.AcceptOffer(null);
            }

            using (cancellationToken.Register(this.AcceptChannelCanceled, channel, false))
            {
                return await channel.Stream.ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
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
                this.completionSource.TrySetResult(null);
                this.TraceInformation("Disposing.");
                this.stream.Dispose();
                lock (this.syncObject)
                {
                    foreach (var entry in this.openChannels)
                    {
                        entry.Value.Dispose();
                    }

                    foreach (var entry in this.channelsOfferedByUs)
                    {
                        entry.Value.Dispose();
                    }

                    this.openChannels.Clear();
                    this.channelsOfferedByUs.Clear();
                    this.channelsOfferedByThem.Clear();
                    this.acceptingChannels.Clear();
                }
            }
        }

        private static async Task<int> ReadAtLeastAsync(Stream stream, ArraySegment<byte> buffer, int requiredLength)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.NotNull(buffer.Array, nameof(buffer));
            Requires.Range(requiredLength >= 0, nameof(requiredLength));

            int bytesRead = 0;
            while (bytesRead < requiredLength)
            {
                int bytesReadJustNow = await stream.ReadAsync(buffer.Array, buffer.Offset + bytesRead, buffer.Count - bytesRead).ConfigureAwait(false);
                if (bytesReadJustNow == 0)
                {
                    // The stream has closed.
                    return bytesRead;
                }

                bytesRead += bytesReadJustNow;
            }

            return bytesRead;
        }

        private async Task ReadStreamAsync()
        {
            var buffer = new byte[FrameHeader.HeaderLength + this.maxFrameLength];
            while (!this.Completion.IsCompleted)
            {
                int totalBytesRead = await this.ReadThisAtLeastAsync(new ArraySegment<byte>(buffer), FrameHeader.HeaderLength).ConfigureAwait(false);
                if (totalBytesRead == 0)
                {
                    this.TraceInformation("The stream ended normally.");
                    break;
                }
                else if (totalBytesRead < FrameHeader.HeaderLength)
                {
                    this.TraceCritical("Unexpected end of stream while reading message.");
                    throw new EndOfStreamException();
                }

                int payloadOffset = FrameHeader.Deserialize(new ArraySegment<byte>(buffer, 0, totalBytesRead), out var header);
                int contentBytesReadAlready = totalBytesRead - FrameHeader.HeaderLength;
                totalBytesRead += await this.ReadThisAtLeastAsync(
                    new ArraySegment<byte>(buffer, totalBytesRead, buffer.Length - totalBytesRead),
                    header.FramePayloadLength - contentBytesReadAlready).ConfigureAwait(false);
                if (totalBytesRead < FrameHeader.HeaderLength + header.FramePayloadLength)
                {
                    this.TraceCritical("Unexpected end of stream while reading message.");
                    throw new EndOfStreamException();
                }

                var contentPayload = new ArraySegment<byte>(buffer, payloadOffset, header.FramePayloadLength);
                this.TraceInformation("Received {0} frame for channel {1} with {2} bytes of content.", header.Code, header.ChannelId, contentPayload.Count);
                switch (header.Code)
                {
                    case ControlCode.CreatingChannel:
                        this.OnCreatingChannel(header.ChannelId, contentPayload);
                        break;
                    case ControlCode.CreatingChannelCanceled:
                        this.OnCreatingChannelCanceled(header.ChannelId, contentPayload);
                        break;
                    case ControlCode.ChannelCreated:
                        this.OnChannelCreated(header.ChannelId, contentPayload);
                        break;
                    case ControlCode.Content:
                        await this.OnContent(header.ChannelId, contentPayload);
                        break;
                    case ControlCode.ChannelTerminated:
                        this.OnChannelTerminated(header.ChannelId);
                        break;
                    default:
                        break;
                }
            }
        }

        private void OnChannelTerminated(int channelId)
        {
            Channel channel;
            lock (this.syncObject)
            {
                if (this.openChannels.TryGetValue(channelId, out channel))
                {
                    this.openChannels.Remove(channelId);
                }
            }

            if (channel != null)
            {
                Assumes.True(channel.Stream.IsCompleted);
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                channel.Stream.Result.RemoteEnded();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
            }
        }

        private ValueTask<FlushResult> OnContent(int channelId, ArraySegment<byte> payload)
        {
            Channel channel;
            lock (this.syncObject)
            {
                if (!this.openChannels.TryGetValue(channelId, out channel))
                {
#if TRACESOURCE
                    this.TraceSource.TraceEvent(TraceEventType.Warning, (int)TraceEventId.MessageReceivedForUnknownChannel, "Message content received for unknown channel {0}.", channelId);
#endif
                    return default;
                }
            }

            Assumes.True(channel.Stream.IsCompleted);
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            return channel.Stream.Result.AddReadMessage(payload);
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
        }

        private void OnChannelCreated(int channelId, ArraySegment<byte> payload)
        {
            Channel channel;
            lock (this.syncObject)
            {
                if (this.channelsOfferedByUs.TryGetValue(channelId, out channel))
                {
                    this.channelsOfferedByUs.Remove(channelId);
                    this.openChannels.Add(channelId, channel);
                }
            }

            if (channel != null)
            {
                channel.Accepted();
            }
            else
            {
                // This may be an acceptance of a channel that we canceled an offer for, and a race condition
                // led to our cancellation notification crossing in transit with their acceptance notification.
                // In this case, all we can do is inform them that we've closed the channel.
                this.SendFrame(ControlCode.ChannelTerminated, channelId);
            }
        }

        private void OnCreatingChannelCanceled(int channelId, ArraySegment<byte> payload)
        {
            Channel channel;
            lock (this.syncObject)
            {
                this.openChannels.TryGetValue(channelId, out channel);
            }

            if (channel != null && channel.TryCancel())
            {
                lock (this.syncObject)
                {
                    this.openChannels.Remove(channelId);
                    if (this.channelsOfferedByThem.TryGetValue(channel.Name, out var queue))
                    {
                        queue.RemoveMidQueue(channel);
                    }
                }
            }
        }

        private void OnCreatingChannel(int channelId, ArraySegment<byte> payload)
        {
            string name = ControlFrameEncoding.GetString(payload.Array, payload.Offset, payload.Count);
            Channel acceptingChannel = null;
            bool acceptingChannelAlreadyPresent = false;
            lock (this.syncObject)
            {
                if (this.acceptingChannels.TryGetValue(name, out var acceptingChannels))
                {
                    while (acceptingChannels.Count > 0)
                    {
                        // TODO: look for race conditions
                        var candidate = acceptingChannels.Dequeue();
                        if (!candidate.Canceled)
                        {
                            this.TraceInformation("Remote party offers channel {1} \"{0}\" which matches up with a pending " + nameof(this.AcceptChannelAsync), name, channelId);
                            acceptingChannel = candidate;
                            acceptingChannelAlreadyPresent = true;
                            break;
                        }
                    }
                }

                if (acceptingChannel == null)
                {
                    this.TraceInformation("Remote party offers channel \"{0}\" which has no pending " + nameof(this.AcceptChannelAsync), name);
                    if (!this.channelsOfferedByThem.TryGetValue(name, out var offeredChannels))
                    {
                        this.channelsOfferedByThem.Add(name, offeredChannels = new Queue<Channel>());
                    }

                    acceptingChannel = new Channel(this, channelId, name);
                    offeredChannels.Enqueue(acceptingChannel);
                }

                this.openChannels.Add(channelId, acceptingChannel);
            }

            if (acceptingChannelAlreadyPresent)
            {
                acceptingChannel.AcceptOffer(channelId);
            }
        }

        private void OnChannelDisposed(Channel channel)
        {
            Requires.NotNull(channel, nameof(channel));

            if (!this.Completion.IsCompleted)
            {
                this.TraceInformation("Local channel \"{0}\" stream disposed.", channel.Name);
                bool removed;
                lock (this.syncObject)
                {
                    removed = this.openChannels.Remove(channel.Id.Value);
                }

                if (removed)
                {
                    this.SendFrame(ControlCode.ChannelTerminated, channel.Id.Value);
                }
            }
        }

        private void RejectChannelDueToConflict(int channelId)
        {
            throw new NotImplementedException();
        }

        private async Task<int> ReadThisAtLeastAsync(ArraySegment<byte> buffer, int requiredLength)
        {
            int bytesRead = await ReadAtLeastAsync(this.stream, buffer, requiredLength).ConfigureAwait(false);
            if (bytesRead < requiredLength)
            {
                // The remote end closed the stream before we got all the bytes we need.
                this.Dispose();
            }

            return bytesRead;
        }

        private void SendFrame(ControlCode code, int channelId)
        {
            var header = new FrameHeader
            {
                Code = code,
                ChannelId = channelId,
                FramePayloadLength = 0,
            };
            this.DisposeSelfOnFailure(this.SendFrameAsync(header, payload: default, flush: true, CancellationToken.None));
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

        private async Task SendFrameAsync(FrameHeader header, ArraySegment<byte> payload, bool flush, CancellationToken cancellationToken)
        {
            Task flushTask = null;
            await this.sendingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                this.TraceInformation("Sending {0} frame for channel {1}, carrying {2} bytes of content.", header.Code, header.ChannelId, payload.Count);
                int headerLength = header.Serialize(new ArraySegment<byte>(this.sendingHeaderBuffer, 0, this.sendingHeaderBuffer.Length));

                // Don't propagate the CancellationToken any more to avoid corrupting the stream with a half-written frame.
                // We're hedging our bets since we don't know whether the Stream will abort a partial write given a canceled token.
                var writeHeaderTask = this.stream.WriteAsync(this.sendingHeaderBuffer, 0, headerLength, CancellationToken.None);
                if (payload.Array != null)
                {
                    await Task.WhenAll(
                        writeHeaderTask,
                        this.stream.WriteAsync(payload.Array, payload.Offset, payload.Count)).ConfigureAwait(false);
                }
                else
                {
                    await writeHeaderTask.ConfigureAwait(false);
                }

                if (flush)
                {
                    flushTask = this.stream.FlushAsync(CancellationToken.None);
                }
            }
            finally
            {
                this.sendingSemaphore.Release();
            }

            if (flushTask != null)
            {
                await flushTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gets a unique number that can be used to represent a channel.
        /// </summary>
        /// <returns>An unused channel number.</returns>
        /// <remarks>
        /// The channel numbers increase by two in order to maintain odd or even numbers, since each party is allowed to create only one or the other.
        /// </remarks>
        private int GetUnusedChannelId() => Interlocked.Add(ref this.lastOfferedChannelId, 2);

        private void CreateChannelCanceled(object state)
        {
            Requires.NotNull(state, nameof(state));
            var channel = (Channel)state;
            bool removed = false;
            lock (this.syncObject)
            {
                if (channel.TryCancel())
                {
                    this.TraceInformation("Cancelling " + nameof(this.CreateChannelAsync) + " for \"{0}\"", channel.Name);
                    removed = this.channelsOfferedByUs.Remove(channel.Id.Value);
                }
                else
                {
                    this.TraceInformation("Cancelling " + nameof(this.CreateChannelAsync) + " for \"{0}\" attempted but failed.", channel.Name);
                }
            }

            if (removed)
            {
                this.SendFrame(ControlCode.CreatingChannelCanceled, channel.Id.Value);
            }
        }

        private void AcceptChannelCanceled(object state)
        {
            Requires.NotNull(state, nameof(state));
            var channel = (Channel)state;
            lock (this.syncObject)
            {
                if (channel.TryCancel())
                {
                    this.TraceInformation("Cancelling " + nameof(this.AcceptChannelAsync) + " for \"{0}\"", channel.Name);
                    if (this.acceptingChannels.TryGetValue(channel.Name, out var queue))
                    {
                        if (queue.Peek() == channel)
                        {
                            queue.Dequeue();
                        }
                    }
                }
                else
                {
                    this.TraceInformation("Cancelling " + nameof(this.AcceptChannelAsync) + " for \"{0}\" attempted but failed.", channel.Name);
                }
            }
        }

        private void DisposeSelfOnFailure(Task task)
        {
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
            this.TraceCritical("Disposing self due to exception: {0}", exception);
            this.completionSource.TrySetException(exception);
            this.Dispose();
        }

        [Conditional("TRACESOURCE")]
        private void TraceCritical(string message)
        {
#if TRACESOURCE
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Critical))
            {
                this.TraceSource.TraceInformation(message);
            }
#endif
        }

        [Conditional("TRACESOURCE")]
        private void TraceCritical(string message, object arg)
        {
#if TRACESOURCE
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Critical))
            {
                this.TraceSource.TraceInformation(message, arg);
            }
#endif
        }

        [Conditional("TRACESOURCE")]
        private void TraceInformation(string message)
        {
#if TRACESOURCE
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                this.TraceSource.TraceInformation(message);
            }
#endif
        }

        [Conditional("TRACESOURCE")]
        private void TraceInformation(string message, object arg)
        {
#if TRACESOURCE
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                this.TraceSource.TraceInformation(message, arg);
            }
#endif
        }

        [Conditional("TRACESOURCE")]
        private void TraceInformation(string message, object arg1, int arg2)
        {
#if TRACESOURCE
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                this.TraceSource.TraceInformation(message, arg1, arg2);
            }
#endif
        }

        [Conditional("TRACESOURCE")]
        private void TraceInformation(string unformattedMessage, ControlCode code, int channelId, int contentLength = 0)
        {
#if TRACESOURCE
            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
            {
                this.TraceSource.TraceInformation(unformattedMessage, code, channelId, contentLength);
            }
#endif
        }
    }
}
