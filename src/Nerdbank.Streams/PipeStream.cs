// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

#pragma warning disable AvoidAsyncSuffix // Avoid Async suffix

    /// <summary>
    /// Wraps a <see cref="PipeReader"/> and/or <see cref="PipeWriter"/> as a <see cref="Stream"/> for
    /// easier interop with existing APIs.
    /// </summary>
    internal class PipeStream : Stream, IDisposableObservable
    {
        /// <summary>
        /// The <see cref="PipeWriter"/> to use when writing to this stream. May be null.
        /// </summary>
        private readonly PipeWriter? writer;

        /// <summary>
        /// The <see cref="PipeReader"/> to use when reading from this stream. May be null.
        /// </summary>
        private readonly PipeReader? reader;

        /// <summary>
        /// A value indicating whether the <see cref="writer"/> and <see cref="reader"/> should be completed when this instance is disposed.
        /// </summary>
        private readonly bool ownsPipe;

        /// <summary>
        /// Indicates whether reading was completed.
        /// </summary>
        private bool readingCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStream"/> class.
        /// </summary>
        /// <param name="writer">The <see cref="PipeWriter"/> to use when writing to this stream. May be null.</param>
        /// <param name="ownsPipe"><c>true</c> to complete the underlying reader and writer when the <see cref="Stream"/> is disposed; <c>false</c> to keep them open.</param>
        internal PipeStream(PipeWriter writer, bool ownsPipe)
        {
            Requires.NotNull(writer, nameof(writer));
            this.writer = writer;
            this.ownsPipe = ownsPipe;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStream"/> class.
        /// </summary>
        /// <param name="reader">The <see cref="PipeReader"/> to use when reading from this stream. May be null.</param>
        /// <param name="ownsPipe"><c>true</c> to complete the underlying reader and writer when the <see cref="Stream"/> is disposed; <c>false</c> to keep them open.</param>
        internal PipeStream(PipeReader reader, bool ownsPipe)
        {
            Requires.NotNull(reader, nameof(reader));
            this.reader = reader;
            this.ownsPipe = ownsPipe;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStream"/> class.
        /// </summary>
        /// <param name="pipe">A full duplex pipe that will serve as the transport for this stream.</param>
        /// <param name="ownsPipe"><c>true</c> to complete the underlying reader and writer when the <see cref="Stream"/> is disposed; <c>false</c> to keep them open.</param>
        internal PipeStream(IDuplexPipe pipe, bool ownsPipe)
        {
            Requires.NotNull(pipe, nameof(pipe));

            this.writer = pipe.Output;
            this.reader = pipe.Input;
            this.ownsPipe = ownsPipe;
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

        /// <summary>
        /// Gets the underlying <see cref="PipeReader"/> (for purposes of unwrapping instead of stacking adapters).
        /// </summary>
        internal PipeReader? UnderlyingPipeReader => this.reader;

        /// <summary>
        /// Gets the underlying <see cref="PipeWriter"/> (for purposes of unwrapping instead of stacking adapters).
        /// </summary>
        internal PipeWriter? UnderlyingPipeWriter => this.writer;

        /// <inheritdoc />
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (this.writer == null)
            {
                throw new NotSupportedException();
            }

            await this.writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset + count <= buffer.Length, nameof(count));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Verify.NotDisposed(this);

            if (this.writer == null)
            {
                throw new NotSupportedException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            this.writer.Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset + count <= buffer.Length, nameof(count));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count > 0, nameof(count));
            Verify.NotDisposed(this);

            if (this.reader == null)
            {
                throw new NotSupportedException();
            }

            if (this.readingCompleted)
            {
                return 0;
            }

            ReadResult readResult = await this.reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return this.ReadHelper(buffer.AsSpan(offset, count), readResult);
        }

#if SPAN_BUILTIN
        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Verify.NotDisposed(this);

            if (this.reader == null)
            {
                throw new NotSupportedException();
            }

            if (this.readingCompleted)
            {
                return 0;
            }

            ReadResult readResult = await this.reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return this.ReadHelper(buffer.Span, readResult);
        }

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            Verify.NotDisposed(this);
            if (this.reader == null)
            {
                throw new NotSupportedException();
            }

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            ReadResult readResult = this.reader.ReadAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
            return this.ReadHelper(buffer, readResult);
        }

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Verify.NotDisposed(this);
            if (this.writer == null)
            {
                throw new NotSupportedException();
            }

            this.writer.Write(buffer.Span);
            return default;
        }

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Verify.NotDisposed(this);
            if (this.writer == null)
            {
                throw new NotSupportedException();
            }

            this.writer.Write(buffer);
        }

#endif

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits

        /// <inheritdoc />
        public override void Flush()
        {
            if (this.writer == null)
            {
                throw new NotSupportedException();
            }

            this.writer.FlushAsync().GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => this.ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => this.WriteAsync(buffer, offset, count).GetAwaiter().GetResult();

#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            this.reader?.CancelPendingRead();
            this.writer?.CancelPendingFlush();
            if (this.ownsPipe)
            {
                this.reader?.Complete();
                this.writer?.Complete();
            }

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

        private int ReadHelper(Span<byte> buffer, ReadResult readResult)
        {
            if (readResult.IsCanceled && this.IsDisposed)
            {
                return 0;
            }

            long bytesToCopyCount = Math.Min(buffer.Length, readResult.Buffer.Length);
            ReadOnlySequence<byte> slice = readResult.Buffer.Slice(0, bytesToCopyCount);
            var isCompleted = readResult.IsCompleted && slice.End.Equals(readResult.Buffer.End);
            slice.CopyTo(buffer);
            this.reader!.AdvanceTo(slice.End);
            readResult.ScrubAfterAdvanceTo();
            slice = default;

            if (isCompleted)
            {
                this.reader.Complete();
                this.readingCompleted = true;
            }

            return (int)bytesToCopyCount;
        }
    }
}
