// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <content>
    /// Contains the <see cref="Channel"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
        private class Channel : IDisposable
        {
            private readonly TaskCompletionSource<ChannelStream> acceptanceSource = new TaskCompletionSource<ChannelStream>();

            internal Channel(MultiplexingStream multiplexingStream, int? id, string name)
            {
                Requires.NotNull(multiplexingStream, nameof(multiplexingStream));
                Requires.NotNull(name, nameof(name));

                this.UnderlyingMultiplexingStream = multiplexingStream;
                this.Id = id;
                this.Name = name;
            }

            internal MultiplexingStream UnderlyingMultiplexingStream { get; }

            internal string Name { get; set; }

            internal int? Id { get; private set; }

            internal Task<ChannelStream> Stream => this.acceptanceSource.Task;

            internal bool Canceled => this.acceptanceSource.Task.IsCanceled;

            private string DebuggerDisplay => $"{Id} {Name}";

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
            internal Stream Accepted()
            {
                Verify.Operation(!this.acceptanceSource.Task.IsCompleted, "Invalid state transition.");
                Assumes.True(this.Id.HasValue);
                ChannelStream stream = new ChannelStream(this);
                this.acceptanceSource.SetResult(stream);
                return stream;
            }

            internal bool TryCancel() => this.acceptanceSource.TrySetCanceled();

            internal void OnStreamDisposed()
            {
                this.UnderlyingMultiplexingStream.OnChannelDisposed(this);
                this.Dispose();
            }
        }
    }
}
