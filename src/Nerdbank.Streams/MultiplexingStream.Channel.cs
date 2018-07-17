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
        public class Channel : IDisposable
        {
            private readonly AsyncManualResetEvent acceptedEvent = new AsyncManualResetEvent();

            internal Channel(MultiplexingStream multiplexingStream, int id, string name)
            {
                Requires.NotNull(multiplexingStream, nameof(multiplexingStream));

                this.UnderlyingMultiplexingStream = multiplexingStream;
                this.Id = id;
                this.Name = name;
            }

            /// <summary>
            /// Gets the unique ID for this channel.
            /// </summary>
            /// <remarks>
            /// For an anonymous channel, this ID may be shared with the remote party
            /// so they can accept it with <see cref="MultiplexingStream.AcceptChannel(int)"/>.
            /// </remarks>
            public int Id { get; }

            /// <summary>
            /// Gets the options that describe treatment of this channel on the local end.
            /// </summary>
            /// <value>A non-null value.</value>
            public ChannelOptions Options { get; }

            internal MultiplexingStream UnderlyingMultiplexingStream { get; }

            internal string Name { get; set; }

            internal bool Canceled => this.acceptanceSource.Task.IsCanceled;

            /// <summary>
            /// Gets a <see cref="Task"/> that completes when the channel is accepted, rejected, or canceled.
            /// </summary>
            /// <remarks>
            /// If the channel is accepted, this task transitions to <see cref="TaskStatus.RanToCompletion"/> state.
            /// If the channel offer is canceled, this task transitions to a <see cref="TaskStatus.Canceled"/> state.
            /// If the channel offer is rejected, this task transitions to a <see cref="TaskStatus.Canceled"/> state.
            /// </remarks>
            internal Task Acceptance => this.acceptedEvent.WaitAsync();

            private string DebuggerDisplay => $"{Id} {Name ?? "(anonymous)"}";

            /// <summary>
            /// Closes this channel and releases all resources associated with it.
            /// </summary>
            public void Dispose()
            {
                // We only need to dispose of the stream if there's a chance it will ever be created.
                // Thus, schedule disposal if we fail to cancel its creation process.
                if (!this.acceptanceSource.TrySetCanceled())
                {
                    this.acceptanceSource.Task.ContinueWith(
                        t => t.Result.Dispose(),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default).Forget();
                }
            }

            /// <summary>
            /// Gets a stream that can send and receive over this channel.
            /// </summary>
            /// <returns>A stream.</returns>
            /// <exception cref="InvalidOperationException">Thrown if <see cref="UsePipe"/> has already been called.</exception>
            public Stream UseStream() => throw new NotImplementedException();

            /// <summary>
            /// Gets a pipe writer and reader that can be used to send and receive over this channel.
            /// </summary>
            /// <returns>A pair of pipe I/O objects.</returns>
            /// <exception cref="InvalidOperationException">Thrown if <see cref="UseStream"/> has already been called.</exception>
            public (PipeReader, PipeWriter) UsePipe() => throw new NotImplementedException();

            /// <summary>
            /// Accepts an offer made by the remote party.
            /// </summary>
            /// <param name="id">The ID of the channel that was accepted. Must not be null if this instance was created before an offer was actually made.</param>
            /// <returns>The created stream for the channel.</returns>
            internal Stream AcceptOffer(int? id)
            {
                Verify.Operation(!this.acceptanceSource.Task.IsCompleted, "Invalid state transition.");
                Requires.Argument(this.Id.HasValue || id.HasValue, nameof(id), "Value required because this channel was created without an id.");
                Requires.Argument(!this.Id.HasValue || !id.HasValue || this.Id.Value == id.Value, nameof(id), "Value is specified but does not match original ID this instance was created with.");

                if (id.HasValue)
                {
                    this.Id = id;
                }

                var stream = new ChannelStream(this);
                this.acceptanceSource.SetResult(stream);
                this.UnderlyingMultiplexingStream.SendFrame(ControlCode.ChannelCreated, this.Id.Value);
                return stream;
            }

            /// <summary>
            /// Called when an offer of a channel made locally has been accepted by the remote party.
            /// </summary>
            internal void Accepted()
            {
                Verify.Operation(!this.acceptanceSource.Task.IsCompleted, "Invalid state transition.");
                Assumes.True(this.Id.HasValue);
                ChannelStream stream = new ChannelStream(this);
                this.acceptanceSource.SetResult(stream);
            }

            internal bool TryCancel() => this.acceptanceSource.TrySetCanceled();

            internal void OnStreamDisposed()
            {
                this.UnderlyingMultiplexingStream.OnChannelDisposed(this);
                this.Dispose();
            }

            internal void RemoteEnded()
            {
                this.Stream.Result.dataReceivedWriter.Complete();
            }

            internal ValueTask<FlushResult> AddReadMessage(ArraySegment<byte> message) => this.Stream.Result.AddReadMessage(message);
        }
    }
}
