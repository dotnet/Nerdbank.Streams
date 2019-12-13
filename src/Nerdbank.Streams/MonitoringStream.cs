// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
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
        /// A value indicating whether the <see cref="EndOfStream"/> event has already been raised.
        /// </summary>
        private bool endOfStreamRaised;

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoringStream"/> class.
        /// </summary>
        /// <param name="inner">The stream to wrap and monitor I/O for.</param>
        public MonitoringStream(Stream inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// A delegate used for events that pass <see cref="Span{T}"/> around.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="span">The span.</param>
        public delegate void SpanEventHandler(object sender, Span<byte> span);

        /// <summary>
        /// A delegate used for events that pass <see cref="ReadOnlySpan{T}"/> around.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="span">The span.</param>
        public delegate void ReadOnlySpanEventHandler(object sender, ReadOnlySpan<byte> span);

        /// <summary>
        /// Occurs the first time a read method returns 0 bytes.
        /// </summary>
        public event EventHandler? EndOfStream;

        /// <summary>
        /// Occurs after <see cref="Seek(long, SeekOrigin)"/> is invoked.
        /// </summary>
        public event EventHandler<long>? DidSeek;

        /// <summary>
        /// Occurs before <see cref="Read(byte[], int, int)"/> or <see cref="ReadAsync(byte[], int, int, CancellationToken)"/> is invoked.
        /// </summary>
        /// <remarks>
        /// The <see cref="ArraySegment{T}.Count"/> value is the maximum bytes that may be read.
        /// </remarks>
        public event EventHandler<ArraySegment<byte>>? WillRead;

        /// <summary>
        /// Occurs after <see cref="Read(byte[], int, int)"/> or <see cref="ReadAsync(byte[], int, int, CancellationToken)"/> is invoked.
        /// </summary>
        /// <remarks>
        /// The <see cref="ArraySegment{T}.Count"/> value is the actual bytes that were read.
        /// </remarks>
        public event EventHandler<ArraySegment<byte>>? DidRead;

#pragma warning disable CS1574
#pragma warning disable CS0067 // Only .NET Core 2.1 raises these

        /// <summary>
        /// Occurs before <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> is invoked.
        /// </summary>
        /// <remarks>
        /// The <see cref="Memory{T}.Length"/> value is the maximum bytes that may be read.
        /// </remarks>
        public event EventHandler<Memory<byte>>? WillReadMemory;

        /// <summary>
        /// Occurs after <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> is invoked.
        /// </summary>
        public event EventHandler<Memory<byte>>? DidReadMemory;

        /// <summary>
        /// Occurs before <see cref="Read(Span{byte})"/> is invoked.
        /// </summary>
        public event SpanEventHandler? WillReadSpan;

        /// <summary>
        /// Occurs after <see cref="Read(Span{byte})"/> is invoked.
        /// </summary>
        public event SpanEventHandler? DidReadSpan;

        /// <summary>
        /// Occurs before <see cref="Write(ReadOnlySpan{byte})"/> is invoked.
        /// </summary>
        public event ReadOnlySpanEventHandler? WillWriteSpan;

        /// <summary>
        /// Occurs after <see cref="Write(ReadOnlySpan{byte})"/> is invoked.
        /// </summary>
        public event ReadOnlySpanEventHandler? DidWriteSpan;

        /// <summary>
        /// Occurs before <see cref="WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> is invoked.
        /// </summary>
        public event EventHandler<ReadOnlyMemory<byte>>? WillWriteMemory;

        /// <summary>
        /// Occurs after <see cref="WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> is invoked.
        /// </summary>
        public event EventHandler<ReadOnlyMemory<byte>>? DidWriteMemory;

#pragma warning restore CS0067 // Only .NET Core 2.1 raises these
#pragma warning restore CS1574

        /// <summary>
        /// Occurs before <see cref="Write(byte[], int, int)"/> or <see cref="WriteAsync(byte[], int, int, CancellationToken)"/> is invoked.
        /// </summary>
        public event EventHandler<ArraySegment<byte>>? WillWrite;

        /// <summary>
        /// Occurs after <see cref="Write(byte[], int, int)"/> or <see cref="WriteAsync(byte[], int, int, CancellationToken)"/> is invoked.
        /// </summary>
        public event EventHandler<ArraySegment<byte>>? DidWrite;

        /// <summary>
        /// Occurs before <see cref="SetLength(long)"/> is invoked.
        /// </summary>
        public event EventHandler<long>? WillSetLength;

        /// <summary>
        /// Occurs after <see cref="SetLength(long)"/> is invoked.
        /// </summary>
        public event EventHandler<long>? DidSetLength;

        /// <summary>
        /// Occurs before <see cref="ReadByte"/> is invoked.
        /// </summary>
        public event EventHandler? WillReadByte;

        /// <summary>
        /// Occurs after <see cref="ReadByte"/> is invoked.
        /// </summary>
        public event EventHandler<int>? DidReadByte;

        /// <summary>
        /// Occurs before <see cref="WriteByte(byte)"/> is invoked.
        /// </summary>
        public event EventHandler<byte>? WillWriteByte;

        /// <summary>
        /// Occurs after <see cref="WriteByte(byte)"/> is invoked.
        /// </summary>
        public event EventHandler<byte>? DidWriteByte;

        /// <summary>
        /// Occurs after <see cref="Flush"/> or <see cref="FlushAsync(CancellationToken)"/> is invoked.
        /// </summary>
        public event EventHandler? DidFlush;

        /// <summary>
        /// Occurs when <see cref="Stream.Dispose()"/> is invoked.
        /// </summary>
        public event EventHandler? Disposed;

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
        public override void Flush()
        {
            this.inner.Flush();
            this.DidFlush?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await this.inner.FlushAsync(cancellationToken).ConfigureAwait(false);
            this.DidFlush?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            this.WillRead?.Invoke(this, new ArraySegment<byte>(buffer, offset, count));
            int bytesRead = this.inner.Read(buffer, offset, count);
            this.DidRead?.Invoke(this, new ArraySegment<byte>(buffer, offset, bytesRead));
            this.RaiseEndOfStreamIfNecessary(bytesRead);
            return bytesRead;
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.WillRead?.Invoke(this, new ArraySegment<byte>(buffer, offset, count));
            int bytesRead = await this.inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            this.DidRead?.Invoke(this, new ArraySegment<byte>(buffer, offset, bytesRead));
            this.RaiseEndOfStreamIfNecessary(bytesRead);
            return bytesRead;
        }

#if SPAN_BUILTIN

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            this.WillReadSpan?.Invoke(this, buffer);
            int bytesRead = base.Read(buffer);
            this.DidReadSpan?.Invoke(this, buffer);
            this.RaiseEndOfStreamIfNecessary(bytesRead);
            return bytesRead;
        }

        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.WillReadMemory?.Invoke(this, buffer);
            int bytesRead = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            this.DidReadMemory?.Invoke(this, buffer);
            this.RaiseEndOfStreamIfNecessary(bytesRead);
            return bytesRead;
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            this.WillWriteSpan?.Invoke(this, buffer);
            base.Write(buffer);
            this.DidWriteSpan?.Invoke(this, buffer);
        }

        /// <inheritdoc/>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.WillWriteMemory?.Invoke(this, buffer);
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            this.DidWriteMemory?.Invoke(this, buffer);
        }

#endif

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
            this.RaiseEndOfStreamIfNecessary(result == -1 ? 0 : 1);
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
            if (disposing)
            {
                this.inner.Dispose();
                this.Disposed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RaiseEndOfStreamIfNecessary(int bytesRead)
        {
            if (bytesRead == 0 && !this.endOfStreamRaised)
            {
                this.EndOfStream?.Invoke(this, EventArgs.Empty);
                this.endOfStreamRaised = true;
            }
        }
    }
}
