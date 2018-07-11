// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

#if WEBSOCKET

namespace Nerdbank.Streams
{
    using System;
    using System.IO;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// Exposes a <see cref="WebSocket"/> as a <see cref="Stream"/>.
    /// </summary>
    public class WebSocketStream : Stream, IDisposableObservable
    {
        /// <summary>
        /// A completed task.
        /// </summary>
        private static readonly Task CompletedTask = Task.FromResult(0);

        /// <summary>
        /// The socket wrapped by this stream.
        /// </summary>
        private readonly WebSocket webSocket;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocketStream"/> class.
        /// </summary>
        /// <param name="webSocket">The web socket to wrap in a stream.</param>
        public WebSocketStream(WebSocket webSocket)
        {
            Requires.NotNull(webSocket, nameof(webSocket));
            this.webSocket = webSocket;
        }

        /// <inheritdoc />
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => !this.IsDisposed;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => !this.IsDisposed;

        /// <inheritdoc />
        public override long Length => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override long Position
        {
            get => throw this.ThrowDisposedOr(new NotSupportedException());
            set => throw this.ThrowDisposedOr(new NotSupportedException());
        }

        /// <summary>
        /// Does nothing, since web sockets do not need to be flushed.
        /// </summary>
        public override void Flush()
        {
        }

        /// <summary>
        /// Does nothing, since web sockets do not need to be flushed.
        /// </summary>
        /// <param name="cancellationToken">An ignored cancellation token.</param>
        /// <returns>A completed task.</returns>
        public override Task FlushAsync(CancellationToken cancellationToken) => CompletedTask;

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Verify.NotDisposed(this);

            if (this.webSocket.CloseStatus.HasValue)
            {
                return 0;
            }

            var result = await this.webSocket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken).ConfigureAwait(false);
            return result.Count;
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Verify.NotDisposed(this);
            return this.webSocket.SendAsync(new ArraySegment<byte>(buffer, offset, count), WebSocketMessageType.Binary, true, cancellationToken);
        }

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => this.ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => this.WriteAsync(buffer, offset, count).GetAwaiter().GetResult();

#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            this.webSocket.Dispose();
            base.Dispose(disposing);
        }

        private Exception ThrowDisposedOr(Exception ex)
        {
            Verify.NotDisposed(this);
            throw ex;
        }
    }
}

#endif
