// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// A stream that allows for reading from another stream up to a given number of bytes.
    /// </summary>
    internal class NestedStream : Stream, IDisposableObservable
    {
        /// <summary>
        /// The stream to read from.
        /// </summary>
        private readonly Stream underlyingStream;

        /// <summary>
        /// The total length of the stream.
        /// </summary>
        private readonly long length;

        /// <summary>
        /// The remaining bytes allowed to be read.
        /// </summary>
        private long remainingBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="NestedStream"/> class.
        /// </summary>
        /// <param name="underlyingStream">The stream to read from.</param>
        /// <param name="length">The number of bytes to read from the parent stream.</param>
        public NestedStream(Stream underlyingStream, long length)
        {
            Requires.NotNull(underlyingStream, nameof(underlyingStream));
            Requires.Range(length >= 0, nameof(length));
            Requires.Argument(underlyingStream.CanRead, nameof(underlyingStream), "Stream must be readable.");

            this.underlyingStream = underlyingStream;
            this.remainingBytes = length;
            this.length = length;
        }

        /// <inheritdoc />
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => !this.IsDisposed;

        /// <inheritdoc />
        public override bool CanSeek
        {
            get
            {
                Verify.NotDisposed(this);
                return this.underlyingStream.CanSeek;
            }
        }

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length
        {
            get
            {
                Verify.NotDisposed(this);

                return this.underlyingStream.CanSeek ?
                    this.length : throw new NotSupportedException();
            }
        }

        /// <inheritdoc />
        public override long Position
        {
            get
            {
                Verify.NotDisposed(this);
                return this.length - this.remainingBytes;
            }
            set => throw this.ThrowDisposedOr(new NotSupportedException());
        }

        /// <inheritdoc />
        public override void Flush() => this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            count = (int)Math.Min(count, this.remainingBytes);

            if (count <= 0)
            {
                return 0;
            }

            int bytesRead = await this.underlyingStream.ReadAsync(buffer, offset, count).ConfigureAwaitRunInline();
            this.remainingBytes -= bytesRead;
            return bytesRead;
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            count = (int)Math.Min(count, this.remainingBytes);

            if (count <= 0)
            {
                return 0;
            }

            int bytesRead = this.underlyingStream.Read(buffer, offset, count);
            this.remainingBytes -= bytesRead;
            return bytesRead;
        }

#if SPAN_BUILTIN
        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // If we're beyond the end of the stream (as the result of a Seek operation), return 0 bytes.
            if (this.remainingBytes < 0)
            {
                return 0;
            }

            buffer = buffer.Slice(0, (int)Math.Min(buffer.Length, this.remainingBytes));

            if (buffer.IsEmpty)
            {
                return 0;
            }

            int bytesRead = await this.underlyingStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            this.remainingBytes -= bytesRead;
            return bytesRead;
        }
#endif

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            Verify.NotDisposed(this);

            if (!this.CanSeek)
            {
                throw new NotSupportedException(Strings.SeekingNotSupported);
            }

            // Recalculate offset relative to the current position
            long newOffset = origin switch
            {
                SeekOrigin.Current => offset,
                SeekOrigin.End => this.length + offset - this.Position,
                SeekOrigin.Begin => offset - this.Position,
                _ => throw new ArgumentOutOfRangeException(nameof(origin), Strings.InvalidSeekOrigin),
            };

            // Determine whether the requested position is within the bounds of the stream
            if (this.Position + newOffset < 0)
            {
                throw new IOException(Strings.SeekBeforeBegin);
            }

            long currentPosition = this.underlyingStream.Position;
            long newPosition = this.underlyingStream.Seek(newOffset, SeekOrigin.Current);
            this.remainingBytes -= newPosition - currentPosition;
            return this.Position;
        }

        /// <inheritdoc />
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Verify.NotDisposed(this);
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            Verify.NotDisposed(this);
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }

        private Exception ThrowDisposedOr(Exception ex)
        {
            Verify.NotDisposed(this);
            throw ex;
        }
    }
}
