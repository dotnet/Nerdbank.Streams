// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

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
        private readonly PipeWriter writer;

        /// <summary>
        /// The <see cref="PipeReader"/> to use when reading from this stream. May be null.
        /// </summary>
        private readonly PipeReader reader;

        /// <summary>
        /// Indicates whether reading was completed.
        /// </summary>
        private bool readingCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStream"/> class.
        /// </summary>
        /// <param name="writer">The <see cref="PipeWriter"/> to use when writing to this stream. May be null.</param>
        internal PipeStream(PipeWriter writer)
        {
            Requires.NotNull(writer, nameof(writer));
            this.writer = writer;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStream"/> class.
        /// </summary>
        /// <param name="reader">The <see cref="PipeReader"/> to use when reading from this stream. May be null.</param>
        internal PipeStream(PipeReader reader)
        {
            Requires.NotNull(reader, nameof(reader));
            this.reader = reader;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStream"/> class.
        /// </summary>
        /// <param name="pipe">A full duplex pipe that will serve as the transport for this stream.</param>
        internal PipeStream(IDuplexPipe pipe)
        {
            Requires.NotNull(pipe, nameof(pipe));

            this.writer = pipe.Output;
            this.reader = pipe.Input;
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
            return TplExtensions.CompletedTask;
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

            ReadResult readResult = await this.reader.ReadAsync(cancellationToken);
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

            ReadResult readResult = await this.reader.ReadAsync(cancellationToken);
            return this.ReadHelper(buffer.Span, readResult);
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
#endif

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
            this.reader?.Complete();
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

        private int ReadHelper(Span<byte> buffer, ReadResult readResult)
        {
            long bytesToCopyCount = Math.Min(buffer.Length, readResult.Buffer.Length);
            ReadOnlySequence<byte> slice = readResult.Buffer.Slice(0, bytesToCopyCount);
            slice.CopyTo(buffer);
            this.reader.AdvanceTo(slice.End);

            if (readResult.IsCompleted)
            {
                this.reader.Complete();
                this.readingCompleted = true;
            }

            return (int)bytesToCopyCount;
        }
    }
}
