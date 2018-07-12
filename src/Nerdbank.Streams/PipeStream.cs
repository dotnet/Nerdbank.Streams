// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// Wraps a <see cref="PipeReader"/> and/or <see cref="PipeWriter"/> as a <see cref="Stream"/> for
    /// easier interop with existing APIs.
    /// </summary>
    public class PipeStream : Stream, IDisposableObservable
    {
        /// <summary>
        /// The <see cref="PipeWriter"/> to use when writing to this stream. May be null.
        /// </summary>
        private readonly PipeWriter writer;

        /// <summary>
        /// The <see cref="PipeReader"/> to use when reading from this stream. May be null.
        /// </summary>
        private readonly PipeReader reader;

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStream"/> class.
        /// </summary>
        /// <param name="writer">The <see cref="PipeWriter"/> to use when writing to this stream. May be null.</param>
        /// <param name="reader">The <see cref="PipeReader"/> to use when reading from this stream. May be null.</param>
        public PipeStream(PipeWriter writer, PipeReader reader)
        {
            this.writer = writer;
            this.reader = reader;
        }

        /// <inheritdoc />
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => !this.IsDisposed && this.reader != null;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => !this.IsDisposed && this.writer != null;

        /// <inheritdoc />
        public override long Length => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override long Position
        {
            get => throw this.ThrowDisposedOr(new NotSupportedException());
            set => throw this.ThrowDisposedOr(new NotSupportedException());
        }

        /// <inheritdoc />
        public override async Task FlushAsync(CancellationToken cancellationToken) => await this.writer.FlushAsync(cancellationToken);

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset + count <= buffer.Length, nameof(count));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Verify.NotDisposed(this);

            await this.writer.WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);
        }

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset + count <= buffer.Length, nameof(count));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count > 0, nameof(count));

            ReadResult readResult = await this.reader.ReadAsync(cancellationToken);
            int bytesRead = 0;
            System.Buffers.ReadOnlySequence<byte> slice = readResult.Buffer.Slice(0, Math.Min(count, readResult.Buffer.Length));
            foreach (ReadOnlyMemory<byte> span in slice)
            {
                int bytesToCopy = Math.Min(count, span.Length);
                span.CopyTo(new Memory<byte>(buffer, offset, bytesToCopy));
                offset += bytesToCopy;
                count -= bytesToCopy;
                bytesRead += bytesToCopy;
            }

            this.reader.AdvanceTo(slice.End);
            return bytesRead;
        }

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits

        /// <inheritdoc />
        public override void Flush() => this.writer.FlushAsync().GetAwaiter().GetResult();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => this.ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => this.WriteAsync(buffer, offset, count).GetAwaiter().GetResult();

#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            this.writer?.Complete();
            base.Dispose(disposing);
        }

        private T ReturnIfNotDisposed<T>(T value)
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
