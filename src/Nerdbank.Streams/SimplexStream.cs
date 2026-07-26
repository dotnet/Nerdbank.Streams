// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.IO.Pipelines;
    using System.Runtime.ExceptionServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// A <see cref="Stream"/> that acts as a queue for bytes, in that what gets written to it
    /// can then be read from it, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This stream buffers written content, as any other .NET stream might do.
    /// To ensure bytes written are available to be read, callers should call <see cref="FlushAsync"/> or <see cref="Flush"/> after writing, and before reading.
    /// Flushing automatically happens when the buffer is filled, so writes will block until the reader reads enough data to make room for the new data.
    /// No flushing occurs when using the <see cref="IBufferWriter{T}"/> interface, so callers must call <see cref="FlushAsync"/> or <see cref="Flush"/> after calling <see cref="IBufferWriter{T}.Advance(int)"/>.
    /// </para>
    /// <para>
    /// This class is thread safe for one concurrent reader and writer.
    /// It is <em>not</em> thread safe for multiple concurrent readers or writers.
    /// Disposal is not thread safe and must be executed exclusively of any concurrent reader or writer.
    /// </para>
    /// </remarks>
    public class SimplexStream : Stream, IBufferWriter<byte>, IDisposableObservable
    {
        /// <summary>
        /// The number of bytes to write before automatically flushing.
        /// </summary>
        private const int AutoFlushThreshold = 4096;

        /// <summary>
        /// The pipe that does all the hard work.
        /// </summary>
        private readonly Pipe pipe;

        /// <summary>
        /// Potential exception passed from writer to reader.
        /// </summary>
        private Exception? error;

        /// <summary>
        /// Whether <see cref="CompleteWriting(Exception?)"/> has been called.
        /// </summary>
        private bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimplexStream"/> class.
        /// </summary>
        /// <remarks>
        /// The default thresholds for pausing and resuming the writer are 32KB and 16KB, respectively.
        /// </remarks>
        public SimplexStream()
            : this(16 * 1024, 32 * 1024)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimplexStream"/> class.
        /// </summary>
        /// <param name="resumeWriterThreshold">The size the buffer must shrink to after hitting <paramref name="pauseWriterThreshold"/> before writing is allowed to resume.</param>
        /// <param name="pauseWriterThreshold">The maximum size the buffer is allowed to grow before write calls are blocked (pending a read that will release buffer space.</param>
        public SimplexStream(int resumeWriterThreshold, int pauseWriterThreshold)
        {
            PipeOptions options = new PipeOptions(
                pauseWriterThreshold: pauseWriterThreshold,
                resumeWriterThreshold: resumeWriterThreshold,
                useSynchronizationContext: false);
            this.pipe = new Pipe(options);
            if (!this.pipe.Writer.CanGetUnflushedBytes)
            {
                throw new NotSupportedException("Pipe writer does not support getting unflushed bytes.");
            }
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
        /// Signals that no more writing will take place, causing readers to receive 0 bytes when asking for any more data.
        /// </summary>
        public void CompleteWriting() => this.CompleteWriting(null);

        /// <summary>
        /// Signals that no more writing will take place, causing readers to receive 0 bytes when asking for any more data.
        /// </summary>
        /// <param name="exception">Exception which will be thrown by the reader when end of this stream is reached.</param>
        public void CompleteWriting(Exception? exception)
        {
            if (this.completed)
            {
                return;
            }

            this.error = exception;
            this.pipe.Writer.Complete();
            this.completed = true;
        }

        /// <inheritdoc />
        public override async Task FlushAsync(CancellationToken cancellationToken) => await this.pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset + count <= buffer.Length, nameof(count));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count >= 0, nameof(count));

            ReadResult readResult = await this.pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            int bytesRead = 0;
            ReadOnlySequence<byte> slice = readResult.Buffer.Slice(0, Math.Min(count, readResult.Buffer.Length));
            foreach (ReadOnlyMemory<byte> span in slice)
            {
                int bytesToCopy = Math.Min(count, span.Length);
                span.CopyTo(new Memory<byte>(buffer, offset, bytesToCopy));
                offset += bytesToCopy;
                count -= bytesToCopy;
                bytesRead += bytesToCopy;
            }

            this.pipe.Reader.AdvanceTo(slice.End);

            // exception is throw when reader reaches same position as writer was at when error was set.
            if (bytesRead == 0 && readResult.IsCompleted && this.error is { } ex)
            {
                // rethrow the exception preserving the original stack trace.
                ExceptionDispatchInfo.Capture(ex).Throw();
            }

            return bytesRead;
        }

        /// <inheritdoc />
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset + count <= buffer.Length, nameof(count));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Verify.NotDisposed(this);

            cancellationToken.ThrowIfCancellationRequested();
            Memory<byte> memory = this.pipe.Writer.GetMemory(count);
            buffer.AsMemory(offset, count).CopyTo(memory);
            this.pipe.Writer.Advance(count);

            // Auto-flush if we've written enough data
            if (this.pipe.Writer.UnflushedBytes >= AutoFlushThreshold)
            {
                await this.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        void IBufferWriter<byte>.Advance(int count) => this.pipe.Writer.Advance(count);

        /// <inheritdoc />
        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint) => this.pipe.Writer.GetMemory(sizeHint);

        /// <inheritdoc />
        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => this.pipe.Writer.GetSpan(sizeHint);

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => this.ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset + count <= buffer.Length, nameof(count));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Verify.NotDisposed(this);

            Memory<byte> memory = this.pipe.Writer.GetMemory(count);
            buffer.AsMemory(offset, count).CopyTo(memory);
            this.pipe.Writer.Advance(count);

            // Auto-flush if we've written enough data
            if (this.pipe.Writer.UnflushedBytes >= AutoFlushThreshold)
            {
                this.Flush();
            }
        }

        /// <inheritdoc />
        public override void Flush() => this.FlushAsync().GetAwaiter().GetResult();

#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            this.pipe.Writer.Complete();
            this.pipe.Reader.Complete();
            base.Dispose(disposing);
        }

        private Exception ThrowDisposedOr(Exception ex)
        {
            Verify.NotDisposed(this);
            throw ex;
        }
    }
}
