// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// A stream that reads substreams of arbitrary length.
    /// </summary>
    internal class SubstreamReader : Stream, IDisposableObservable
    {
        private readonly Stream underlyingStream;
        private readonly byte[] intBuffer = new byte[4];
        private int count;
        private bool eof;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubstreamReader"/> class.
        /// </summary>
        /// <param name="underlyingStream">The stream to read from.</param>
        internal SubstreamReader(Stream underlyingStream)
        {
            Requires.NotNull(underlyingStream, nameof(underlyingStream));
            Requires.Argument(underlyingStream.CanRead, nameof(underlyingStream), "Stream must be readable.");

            this.underlyingStream = underlyingStream;
        }

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

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

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public override void Flush() => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;
            if (this.count == 0 && !this.eof)
            {
                while (bytesRead < 4)
                {
                    int bytesJustRead = this.underlyingStream.Read(this.intBuffer, 0, 4 - bytesRead);
                    if (bytesJustRead == 0)
                    {
                        throw new EndOfStreamException();
                    }

                    bytesRead += bytesJustRead;
                }

                this.count = Utilities.ReadInt(this.intBuffer);

                this.eof = this.count == 0;
            }

            if (this.eof)
            {
                return 0;
            }

            bytesRead = this.underlyingStream.Read(buffer, offset, Math.Min(count, this.count));
            this.count -= bytesRead;
            return bytesRead;
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = 0;
            if (this.count == 0 && !this.eof)
            {
                while (bytesRead < 4)
                {
                    int bytesJustRead = await this.underlyingStream.ReadAsync(this.intBuffer, 0, 4 - bytesRead).ConfigureAwait(false);
                    if (bytesJustRead == 0)
                    {
                        throw new EndOfStreamException();
                    }

                    bytesRead += bytesJustRead;
                }

                this.count = Utilities.ReadInt(this.intBuffer);

                this.eof = this.count == 0;
            }

            if (this.eof)
            {
                return 0;
            }

            bytesRead = await this.underlyingStream.ReadAsync(buffer, offset, Math.Min(count, this.count)).ConfigureAwait(false);
            this.count -= bytesRead;
            return bytesRead;
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
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
