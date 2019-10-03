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
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// A stream that writes substreams of arbitrary length.
    /// Created with <see cref="StreamExtensions.WriteSubstream(System.IO.Stream, int)"/>
    /// and later read with <see cref="StreamExtensions.ReadSubstream(System.IO.Stream)"/>.
    /// </summary>
    public class Substream : Stream, IDisposableObservable, IAsyncDisposable
    {
        internal const int DefaultBufferSize = 4096;

        private readonly Stream underlyingStream;

#pragma warning disable SA1011 // Closing square brackets should be spaced correctly
        private byte[]? buffer;
#pragma warning restore SA1011 // Closing square brackets should be spaced correctly

        private int count;

        internal Substream(Stream underlyingStream, int minimumBufferSize = DefaultBufferSize)
        {
            Requires.NotNull(underlyingStream, nameof(underlyingStream));
            Requires.Argument(underlyingStream.CanWrite, nameof(underlyingStream), "Stream must be writable.");

            this.underlyingStream = underlyingStream;
            this.buffer = ArrayPool<byte>.Shared.Rent(minimumBufferSize);
        }

        /// <inheritdoc/>
        public bool IsDisposed => this.buffer == null;

        /// <inheritdoc/>
        public override bool CanRead => false;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => !this.IsDisposed;

        /// <inheritdoc/>
        public override bool CanTimeout => this.underlyingStream.CanTimeout;

        /// <inheritdoc/>
        public override long Length => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override long Position
        {
            get => throw this.ThrowDisposedOr(new NotSupportedException());
            set => throw this.ThrowDisposedOr(new NotSupportedException());
        }

#pragma warning disable AvoidAsyncSuffix // Avoid Async suffix

        /// <inheritdoc/>
        public Task DisposeAsync() => this.DisposeAsync(CancellationToken.None).AsTask();

        /// <summary>
        /// Flushes any buffers, and writes the bytes required to indicate that this substream is at its end.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task to represent the async operation.</returns>
        public async ValueTask DisposeAsync(CancellationToken cancellationToken = default)
        {
            // Write out and clear any buffered data.
            await this.FlushAsync(flushUnderlyingStream: false, cancellationToken).ConfigureAwait(false);

            // Write out that this is the end of the substream by emitting a int32=0 value.
            Array.Clear(this.buffer, 0, 4);
            await this.underlyingStream.WriteAsync(this.buffer, 0, 4, cancellationToken).ConfigureAwait(false);
            await this.underlyingStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            ArrayPool<byte>.Shared.Return(this.buffer);
            this.buffer = null;

            this.Dispose();
        }

#pragma warning restore AvoidAsyncSuffix // Avoid Async suffix

        /// <inheritdoc/>
        public override void Flush() => this.Flush(flushUnderlyingStream: true);

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => this.FlushAsync(flushUnderlyingStream: true, cancellationToken);

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset >= 0 && offset <= buffer.Length, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Requires.Range(offset + count <= buffer.Length, nameof(count));

            Verify.NotDisposed(this);

            int slack = this.buffer!.Length - this.count;
            if (count <= slack)
            {
                Array.Copy(buffer, offset, this.buffer, this.count, count);
                this.count += count;
            }
            else
            {
                int totalCount = this.count + count;
                this.WriteLengthHeader(totalCount);
                this.underlyingStream.Write(this.buffer, 0, this.count);
                this.underlyingStream.Write(buffer, offset, count);
                this.count = 0;
            }
        }

        /// <inheritdoc/>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset >= 0 && offset <= buffer.Length, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Requires.Range(offset + count <= buffer.Length, nameof(count));

            Verify.NotDisposed(this);

            int slack = this.buffer!.Length - this.count;
            if (count <= slack)
            {
                Array.Copy(buffer, offset, this.buffer, this.count, count);
                this.count += count;
            }
            else
            {
                int totalCount = this.count + count;
                await this.WriteLengthHeaderAsync(totalCount, cancellationToken).ConfigureAwait(false);
                await this.underlyingStream.WriteAsync(this.buffer, 0, this.count).ConfigureAwait(false);
                await this.underlyingStream.WriteAsync(buffer, offset, count).ConfigureAwait(false);
                this.count = 0;
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!this.IsDisposed)
                {
                    this.Flush(flushUnderlyingStream: false);

                    // Write out that this is the end of the substream by emitting a int32=0 value.
                    Array.Clear(this.buffer, 0, 4);
                    this.underlyingStream.Write(this.buffer, 0, 4);
                    this.underlyingStream.Flush();

                    ArrayPool<byte>.Shared.Return(this.buffer);
                    this.buffer = null;
                }
            }

            base.Dispose(disposing);
        }

        private void Flush(bool flushUnderlyingStream)
        {
            if (this.count > 0)
            {
                this.WriteLengthHeader(this.count);
                this.underlyingStream.Write(this.buffer, 0, this.count);
                if (flushUnderlyingStream)
                {
                    this.underlyingStream.Flush();
                }

                this.count = 0;
            }
        }

        private async Task FlushAsync(bool flushUnderlyingStream, CancellationToken cancellationToken)
        {
            if (this.count > 0)
            {
                await this.WriteLengthHeaderAsync(this.count, cancellationToken).ConfigureAwait(false);
                await this.underlyingStream.WriteAsync(this.buffer, 0, this.count, cancellationToken).ConfigureAwait(false);
                if (flushUnderlyingStream)
                {
                    await this.underlyingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                this.count = 0;
            }
        }

        private void WriteLengthHeader(int length)
        {
            Span<byte> intBytes = stackalloc byte[4];
            Utilities.Write(intBytes, length);
#if SPAN_BUILTIN
            this.underlyingStream.Write(intBytes);
#else
            this.underlyingStream.Write(intBytes.ToArray(), 0, intBytes.Length);
#endif
        }

#pragma warning disable AvoidAsyncSuffix // Avoid Async suffix
        private async ValueTask WriteLengthHeaderAsync(int length, CancellationToken cancellationToken)
#pragma warning restore AvoidAsyncSuffix // Avoid Async suffix
        {
            byte[] intBytes = ArrayPool<byte>.Shared.Rent(sizeof(int));
            Utilities.Write(intBytes, length);
            try
            {
#if SPAN_BUILTIN
                await this.underlyingStream.WriteAsync(intBytes.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
#else
                await this.underlyingStream.WriteAsync(intBytes, 0, sizeof(int), cancellationToken).ConfigureAwait(false);
#endif
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(intBytes);
            }
        }

        private Exception ThrowDisposedOr(Exception ex)
        {
            Verify.NotDisposed(this);
            throw ex;
        }
    }
}
