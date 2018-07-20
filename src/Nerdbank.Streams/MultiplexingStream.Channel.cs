// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <content>
    /// Contains the <see cref="Channel"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        /// <summary>
        /// An individual channel within a <see cref="MultiplexingStream"/>.
        /// </summary>
        [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
        public class Channel : IDisposableObservable, IDuplexPipe
        {
            /// <summary>
            /// This task source completes when the channel has been accepted, rejected, or the offer is canceled.
            /// </summary>
            private readonly TaskCompletionSource<object> acceptanceSource = new TaskCompletionSource<object>();

            /// <summary>
            /// This pipe relays remote party's data to the local channel owner.
            /// </summary>
            private readonly Pipe receivingPipe;

            /// <summary>
            /// This pipe relays local transmissions to the remote party.
            /// </summary>
            private readonly Pipe transmissionPipe;

            internal Channel(MultiplexingStream multiplexingStream, int id, string name, bool offeredByRemote, ChannelOptions channelOptions)
            {
                Requires.NotNull(multiplexingStream, nameof(multiplexingStream));
                Requires.NotNull(channelOptions, nameof(channelOptions));

                this.UnderlyingMultiplexingStream = multiplexingStream;
                this.Id = id;
                this.Name = name;
                this.OfferedByThem = offeredByRemote;
#if TRACESOURCE
                this.TraceSource = channelOptions.TraceSource ?? new TraceSource($"{nameof(MultiplexingStream)}.{nameof(Channel)} {id} ({name})", SourceLevels.Critical);
#endif

                this.receivingPipe = new Pipe();
                this.transmissionPipe = new Pipe();
                this.DisposeSelfOnFailure(this.ProcessOutboundTransmissionsAsync());
                this.DisposeSelfOnFailure(this.AutoCloseOnPipesClosureAsync());
            }

            /// <summary>
            /// Gets the unique ID for this channel.
            /// </summary>
            /// <remarks>
            /// This value is usually shared for an anonymous channel so the remote party
            /// can accept it with <see cref="AcceptChannel(int, ChannelOptions)"/> or
            /// reject it with <see cref="RejectChannel(int)"/>.
            /// </remarks>
            public int Id { get; }

#if TRACESOURCE
            /// <summary>
            /// Gets the mechanism used for tracing activity related to this channel.
            /// </summary>
            /// <value>A non-null value.</value>
            public TraceSource TraceSource { get; private set; }
#endif

            /// <inheritdoc />
            public bool IsDisposed { get; private set; }

            /// <inheritdoc />
            public PipeReader Input => this.receivingPipe.Reader;

            /// <inheritdoc />
            public PipeWriter Output => this.transmissionPipe.Writer;

            /// <summary>
            /// Gets a <see cref="Task"/> that completes when the channel is accepted, rejected, or canceled.
            /// </summary>
            /// <remarks>
            /// If the channel is accepted, this task transitions to <see cref="TaskStatus.RanToCompletion"/> state.
            /// If the channel offer is canceled, this task transitions to a <see cref="TaskStatus.Canceled"/> state.
            /// If the channel offer is rejected, this task transitions to a <see cref="TaskStatus.Canceled"/> state.
            /// </remarks>
            public Task Acceptance => this.acceptanceSource.Task;

            internal MultiplexingStream UnderlyingMultiplexingStream { get; }

            internal string Name { get; set; }

            /// <summary>
            /// Gets a value indicating whether this channel was originally offered by the remote party.
            /// </summary>
            internal bool OfferedByThem { get; }

            /// <summary>
            /// Gets a value indicating whether this channel was originally offered by us.
            /// </summary>
            internal bool OfferedByUs => !this.OfferedByThem;

            internal bool IsAccepted => this.Acceptance.Status == TaskStatus.RanToCompletion;

            internal bool IsRejectedOrCanceled => this.Acceptance.Status == TaskStatus.Canceled;

            /// <summary>
            /// Gets the pipe writer to use when a message is received for this channel, so that the channel owner will notice and read it.
            /// </summary>
            internal PipeWriter ReceivedMessagePipeWriter => this.receivingPipe.Writer;

            private string DebuggerDisplay => $"{this.Id} {this.Name ?? "(anonymous)"}";

            /// <summary>
            /// Closes this channel and releases all resources associated with it.
            /// </summary>
            public void Dispose()
            {
                if (!this.IsDisposed)
                {
                    this.IsDisposed = true;

                    this.UnderlyingMultiplexingStream.OnChannelDisposed(this);

                    this.acceptanceSource.TrySetCanceled();

                    // For the pipes, we Complete *our* ends, and leave the user's ends alone. The completion will propagate when it's ready to.
                    this.receivingPipe.Writer.Complete();
                    this.transmissionPipe.Reader.Complete();
                }
            }

            /// <summary>
            /// Accepts an offer made by the remote party.
            /// </summary>
            /// <param name="options">The options to apply to the channel.</param>
            /// <returns>A value indicating whether the offer was accepted. It may fail if the channel was already closed or the offer rescinded.</returns>
            internal bool TryAcceptOffer(ChannelOptions options)
            {
                if (this.acceptanceSource.TrySetResult(null))
                {
                    if (options != null)
                    {
#if TRACESOURCE
                        if (options.TraceSource != null)
                        {
                            this.TraceSource = options.TraceSource;
                        }
#endif
                    }

                    return true;
                }

                return false;
            }

            internal void OnStreamDisposed()
            {
                this.UnderlyingMultiplexingStream.OnChannelDisposed(this);
                this.Dispose();
            }

            /// <summary>
            /// Occurs when the remote party has accepted our offer of this channel.
            /// </summary>
            /// <returns>A value indicating whether the acceptance went through; <c>false</c> if the channel is already accepted, rejected or offer rescinded.</returns>
            internal bool OnAccepted() => this.acceptanceSource.TrySetResult(null);

            /// <summary>
            /// Relays data that the local channel owner wants to send to the remote party.
            /// </summary>
            private async Task ProcessOutboundTransmissionsAsync()
            {
                while (!this.IsDisposed)
                {
                    var result = await this.transmissionPipe.Reader.ReadAsync();

                    // We'll send whatever we've got, up to the maximum size of the frame.
                    // Anything in excess of that we'll pick up next time the loop runs.
                    var bufferToRelay = result.Buffer.Slice(0, Math.Min(result.Buffer.Length, this.UnderlyingMultiplexingStream.framePayloadMaxLength));

                    if (bufferToRelay.Length > 0)
                    {
                        FrameHeader header = new FrameHeader
                        {
                            Code = ControlCode.Content,
                            ChannelId = this.Id,
                            FramePayloadLength = (int)bufferToRelay.Length,
                        };

                        await this.UnderlyingMultiplexingStream.SendFrameAsync(header, bufferToRelay, CancellationToken.None).ConfigureAwait(false);

                        // Let the pipe know exactly how much we read, which might be less than we were given.
                        this.transmissionPipe.Reader.AdvanceTo(bufferToRelay.End);
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }

                this.transmissionPipe.Reader.Complete();
            }

            private async Task AutoCloseOnPipesClosureAsync()
            {
#if TRACESOURCE
                this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelAutoClosing, "Channel self-closing because both parties have completed transmission.");
#endif

                // We want to close when all *readers* are completed, so that there is adequate chance for all transmissions to have been fully received.
                await Task.WhenAll(this.transmissionPipe.Writer.WaitForReaderCompletionAsync(), this.receivingPipe.Writer.WaitForReaderCompletionAsync()).ConfigureAwait(false);

                this.Dispose();
            }

            private void Fault(Exception exception)
            {
#if TRACESOURCE
                this.UnderlyingMultiplexingStream.TraceCritical(TraceEventId.FatalError, "Channel Closing self due to exception: {0}", exception);
#endif
                this.transmissionPipe.Reader.Complete(exception);
                this.Dispose();
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
                        (t, s) => ((Channel)s).Fault(t.Exception.InnerException ?? t.Exception),
                        this,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default).Forget();
                }
            }
        }
    }
}
