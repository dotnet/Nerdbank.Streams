// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A <see cref="Stream"/> that wraps another and reports all I/O taking place by raising events.
    /// </summary>
    public class MonitoringStream : Stream
    {
        /// <summary>
        /// The underlying stream serving the I/O.
        /// </summary>
        private readonly Stream inner;

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoringStream"/> class.
        /// </summary>
        /// <param name="inner">The stream to wrap and monitor I/O for.</param>
        public MonitoringStream(Stream inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// Occurs after <see cref="Seek(long, SeekOrigin)"/> is invoked.
        /// </summary>
        public event EventHandler<long> DidSeek;

        /// <summary>
        /// Occurs before <see cref="Read(byte[], int, int)"/> or <see cref="ReadAsync(byte[], int, int, CancellationToken)"/> is invoked.
        /// </summary>
        /// <remarks>
        /// The <see cref="ArraySegment{T}.Count"/> value is the maximum bytes that may be read.
        /// </remarks>
        public event EventHandler<ArraySegment<byte>> WillRead;

        /// <summary>
        /// Occurs after <see cref="Read(byte[], int, int)"/> or <see cref="ReadAsync(byte[], int, int, CancellationToken)"/> is invoked.
        /// </summary>
        /// <remarks>
        /// The <see cref="ArraySegment{T}.Count"/> value is the actual bytes that were read.
        /// </remarks>
        public event EventHandler<ArraySegment<byte>> DidRead;

        /// <summary>
        /// Occurs before <see cref="Write(byte[], int, int)"/> or <see cref="WriteAsync(byte[], int, int, CancellationToken)"/> is invoked.
        /// </summary>
        public event EventHandler<ArraySegment<byte>> WillWrite;

        /// <summary>
        /// Occurs after <see cref="Write(byte[], int, int)"/> or <see cref="WriteAsync(byte[], int, int, CancellationToken)"/> is invoked.
        /// </summary>
        public event EventHandler<ArraySegment<byte>> DidWrite;

        /// <summary>
        /// Occurs before <see cref="SetLength(long)"/> is invoked.
        /// </summary>
        public event EventHandler<long> WillSetLength;

        /// <summary>
        /// Occurs after <see cref="SetLength(long)"/> is invoked.
        /// </summary>
        public event EventHandler<long> DidSetLength;

        /// <summary>
        /// Occurs before <see cref="ReadByte"/> is invoked.
        /// </summary>
        public event EventHandler WillReadByte;

        /// <summary>
        /// Occurs after <see cref="ReadByte"/> is invoked.
        /// </summary>
        public event EventHandler<int> DidReadByte;

        /// <summary>
        /// Occurs before <see cref="WriteByte(byte)"/> is invoked.
        /// </summary>
        public event EventHandler<byte> WillWriteByte;

        /// <summary>
        /// Occurs after <see cref="WriteByte(byte)"/> is invoked.
        /// </summary>
        public event EventHandler<byte> DidWriteByte;

        /// <summary>
        /// Occurs when <see cref="Stream.Dispose()"/> is invoked.
        /// </summary>
        public event EventHandler Disposed;

        /// <inheritdoc/>
        public override bool CanRead => this.inner.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => this.inner.CanSeek;

        /// <inheritdoc/>
        public override bool CanWrite => this.inner.CanWrite;

        /// <inheritdoc/>
        public override long Length => this.inner.Length;

        /// <inheritdoc/>
        public override long Position
        {
            get => this.inner.Position;
            set => this.inner.Position = value;
        }

        /// <inheritdoc/>
        public override int ReadTimeout
        {
            get => this.inner.ReadTimeout;
            set => this.inner.ReadTimeout = value;
        }

        /// <inheritdoc/>
        public override int WriteTimeout
        {
            get => this.inner.WriteTimeout;
            set => this.inner.WriteTimeout = value;
        }

        /// <inheritdoc/>
        public override bool CanTimeout => this.inner.CanTimeout;

        /// <inheritdoc/>
        public override void Flush() => this.inner.Flush();

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => this.inner.FlushAsync(cancellationToken);

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            this.WillRead?.Invoke(this, new ArraySegment<byte>(buffer, offset, count));
            int bytesRead = this.inner.Read(buffer, offset, count);
            this.DidRead?.Invoke(this, new ArraySegment<byte>(buffer, offset, bytesRead));
            return bytesRead;
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.WillRead?.Invoke(this, new ArraySegment<byte>(buffer, offset, count));
            int bytesRead = await this.inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            this.DidRead?.Invoke(this, new ArraySegment<byte>(buffer, offset, bytesRead));
            return bytesRead;
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            long position = this.inner.Seek(offset, origin);
            this.DidSeek?.Invoke(this, position);
            return position;
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            this.WillSetLength?.Invoke(this, value);
            this.inner.SetLength(value);
            this.DidSetLength?.Invoke(this, value);
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WillWrite?.Invoke(this, new ArraySegment<byte>(buffer, offset, count));
            this.inner.Write(buffer, offset, count);
            this.DidWrite?.Invoke(this, new ArraySegment<byte>(buffer, offset, count));
        }

        /// <inheritdoc/>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.WillWrite?.Invoke(this, new ArraySegment<byte>(buffer, offset, count));
            await this.inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            this.DidWrite?.Invoke(this, new ArraySegment<byte>(buffer, offset, count));
        }

        /// <inheritdoc/>
        public override int ReadByte()
        {
            this.WillReadByte?.Invoke(this, EventArgs.Empty);
            int result = this.inner.ReadByte();
            this.DidReadByte?.Invoke(this, result);
            return result;
        }

        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            this.WillWriteByte?.Invoke(this, value);
            this.inner.WriteByte(value);
            this.DidWriteByte?.Invoke(this, value);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            this.Disposed?.Invoke(this, EventArgs.Empty);
        }
    }
}
