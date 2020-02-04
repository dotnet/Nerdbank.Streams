// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// A <see cref="PipeWriter"/> that writes to an underlying <see cref="Stream"/>
    /// when <see cref="FlushAsync(CancellationToken)"/> is called rather than asynchronously sometime later.
    /// </summary>
    internal class StreamPipeWriter : PipeWriter
    {
        private readonly object syncObject = new object();

        private readonly Stream stream;

        private readonly Sequence<byte> buffer = new Sequence<byte>();

        private readonly AsyncSemaphore flushingSemaphore = new AsyncSemaphore(1);

        private List<(Action<Exception?, object?>, object?)>? readerCompletedCallbacks;

        private CancellationTokenSource? flushCancellationSource;

        private bool isReaderCompleted;

        private Exception? readerException;

        private bool isWriterCompleted;

        private Exception? writerException;

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamPipeWriter"/> class.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        internal StreamPipeWriter(Stream stream)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanWrite, nameof(stream), "Stream must be writable.");
            this.stream = stream;
        }

        /// <inheritdoc />
        public override void Advance(int bytes)
        {
            if (bytes > 0)
            {
                Verify.Operation(!this.isWriterCompleted, "Writing is already completed.");
                this.buffer.Advance(bytes);
            }
        }

        /// <inheritdoc />
        public override void CancelPendingFlush() => this.flushCancellationSource?.Cancel();

        /// <inheritdoc />
        public override void Complete(Exception? exception = null)
        {
            lock (this.syncObject)
            {
                if (!this.isWriterCompleted)
                {
                    this.isWriterCompleted = true;
                    this.writerException = exception;
                }
            }

            if (this.buffer.AsReadOnlySequence.IsEmpty)
            {
                this.buffer.Reset();
                this.CompleteReading(null);
            }
            else
            {
                // Completing with unflushed buffers should implicitly flush.
                var nowait = this.FlushAsync();
            }
        }

        /// <inheritdoc />
#pragma warning disable AvoidAsyncSuffix // Avoid Async suffix
        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
#pragma warning restore AvoidAsyncSuffix // Avoid Async suffix
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.flushCancellationSource?.IsCancellationRequested ?? true)
            {
                this.flushCancellationSource = new CancellationTokenSource();
            }

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.flushCancellationSource!.Token))
            {
                using (await this.flushingSemaphore.EnterAsync(cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        while (this.buffer.Length > 0)
                        {
                            // Allow cancellation in between each write, but not during one.
                            // That way we don't corrupt the outbound stream.
                            cts.Token.ThrowIfCancellationRequested();
                            var readOnlySeq = this.buffer.AsReadOnlySequence;
                            var segment = readOnlySeq.First;
                            await this.stream.WriteAsync(segment).ConfigureAwait(false);
                            this.buffer.AdvanceTo(readOnlySeq.GetPosition(segment.Length));
                        }

                        // Presumably, cancelling during a flush doesn't leave the stream in a corrupted state.
                        cts.Token.ThrowIfCancellationRequested();
                        await this.stream.FlushAsync(cts.Token).ConfigureAwait(false);

                        if (this.isWriterCompleted)
                        {
                            this.buffer.Reset();
                            this.CompleteReading();
                        }

                        return new FlushResult(isCanceled: false, isCompleted: true);
                    }
                    catch (OperationCanceledException) when (this.flushCancellationSource.Token.IsCancellationRequested)
                    {
                        return new FlushResult(isCanceled: true, isCompleted: false);
                    }
                }
            }
        }

        /// <inheritdoc />
        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            Verify.Operation(!this.isWriterCompleted, "Writing is already completed.");
            return this.buffer.GetMemory(sizeHint);
        }

        /// <inheritdoc />
        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            Verify.Operation(!this.isWriterCompleted, "Writing is already completed.");
            return this.buffer.GetSpan(sizeHint);
        }

        /// <inheritdoc />
        [Obsolete]
        public override void OnReaderCompleted(Action<Exception?, object?> callback, object? state)
        {
            bool invokeNow;
            lock (this.syncObject)
            {
                if (this.isWriterCompleted)
                {
                    invokeNow = true;
                }
                else
                {
                    invokeNow = false;
                    if (this.readerCompletedCallbacks == null)
                    {
                        this.readerCompletedCallbacks = new List<(Action<Exception?, object?>, object?)>();
                    }

                    this.readerCompletedCallbacks.Add((callback, state));
                }
            }

            if (invokeNow)
            {
                callback(this.writerException, state);
            }
        }

        private void CompleteReading(Exception? readerException = null)
        {
            List<(Action<Exception?, object?>, object?)>? readerCompletedCallbacks = null;
            lock (this.syncObject)
            {
                if (!this.isReaderCompleted)
                {
                    this.isReaderCompleted = true;
                    this.readerException = readerException;

                    readerCompletedCallbacks = this.readerCompletedCallbacks;
                    this.readerCompletedCallbacks = null;
                }
            }

            if (readerCompletedCallbacks != null)
            {
                foreach (var callback in readerCompletedCallbacks)
                {
                    try
                    {
                        callback.Item1(readerException, callback.Item2);
                    }
                    catch
                    {
                        // Swallow each exception.
                    }
                }
            }
        }
    }
}
