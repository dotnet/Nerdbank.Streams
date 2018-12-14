// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// A stream that writes to a <see cref="IBufferWriter{T}"/> of <see cref="byte"/>.
    /// </summary>
    internal class BufferWriterStream : Stream, IDisposableObservable
    {
        private IBufferWriter<byte> writer;

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferWriterStream"/> class.
        /// </summary>
        /// <param name="writer">The writer to write to.</param>
        internal BufferWriterStream(IBufferWriter<byte> writer)
        {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <inheritdoc/>
        public override bool CanRead => false;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => !this.IsDisposed;

        /// <inheritdoc/>
        public override long Length => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override long Position
        {
            get => throw this.ThrowDisposedOr(new NotSupportedException());
            set => this.ThrowDisposedOr(new NotSupportedException());
        }

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public override void Flush() => Verify.NotDisposed(this);

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => this.ReturnOrThrowDisposed(Task.CompletedTask);

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override int ReadByte() => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void SetLength(long value) => this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Verify.NotDisposed(this);

            var span = this.writer.GetSpan(count);
            buffer.AsSpan(offset, count).CopyTo(span);
            this.writer.Advance(count);
        }

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            Verify.NotDisposed(this);
            var span = this.writer.GetSpan(1);
            span[0] = value;
            this.writer.Advance(1);
        }

#if NETCOREAPP2_1

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Verify.NotDisposed(this);
            var span = this.writer.GetSpan(buffer.Length);
            buffer.CopyTo(span);
            this.writer.Advance(buffer.Length);
        }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Write(buffer.Span);
            return default;
        }

#endif

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }

        private T ReturnOrThrowDisposed<T>(T value)
        {
            Verify.NotDisposed(this);
            return value;
        }

        private Exception ThrowDisposedOr(Exception ex)
        {
            Verify.NotDisposed(this);
            throw ex;
        }
    }
}