// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.IO.Compression;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    internal class NestedPipeReader : PipeReader
    {
        private readonly PipeReader pipeReader;
        private long length;
        private long consumedLength;
        private long examinedLength;
        private ReadResult resultOfPriorRead;
        private bool nextReadCanceled;
        private bool readerCompleted;

        public NestedPipeReader(PipeReader pipeReader, long length)
        {
            Requires.NotNull(pipeReader, nameof(pipeReader));
            Requires.Range(length >= 0, nameof(length));

            this.pipeReader = pipeReader;
            this.length = length;
        }

        private long RemainingLength => this.length - this.consumedLength;

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed)
        {
            if (this.Consumed(consumed, consumed))
            {
                this.pipeReader.AdvanceTo(consumed);

                // When we call AdvanceTo on the underlying reader, we're not allowed to reference their buffer any more, so clear it to be safe.
                this.resultOfPriorRead = new ReadResult(default, isCanceled: false, isCompleted: this.resultOfPriorRead.IsCompleted);
            }
        }

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            if (this.Consumed(consumed, examined))
            {
                this.pipeReader.AdvanceTo(consumed, examined);

                // When we call AdvanceTo on the underlying reader, we're not allowed to reference their buffer any more, so clear it to be safe.
                this.resultOfPriorRead = new ReadResult(default, isCanceled: false, isCompleted: this.resultOfPriorRead.IsCompleted);
            }
        }

        /// <inheritdoc/>
        public override void CancelPendingRead()
        {
            if (this.resultOfPriorRead.IsCompleted)
            {
                this.nextReadCanceled = true;
            }
            else
            {
                this.pipeReader.CancelPendingRead();
            }
        }

        /// <inheritdoc/>
        public override void Complete(Exception exception = null)
        {
            // We don't want a nested PipeReader to complete the underlying one unless there was an exception.
            if (exception != null)
            {
                this.pipeReader.Complete(exception);
            }

            this.readerCompleted = true;
        }

        /// <inheritdoc/>
        public override void OnWriterCompleted(Action<Exception, object> callback, object state)
        {
            // We will never call this back. The method is deprecated in .NET Core 3.0 anyway.
        }

        /// <inheritdoc/>
        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            Verify.Operation(!this.readerCompleted, Strings.ReadingAfterCompletionNotAllowed);
            if (this.resultOfPriorRead.IsCompleted)
            {
                var cachedResult = this.resultOfPriorRead = new ReadResult(this.resultOfPriorRead.Buffer, isCanceled: this.nextReadCanceled, isCompleted: true);
                this.nextReadCanceled = false;
                return cachedResult;
            }

            var result = await this.pipeReader.ReadAsync(cancellationToken);
            if (result.Buffer.Length >= this.RemainingLength)
            {
                result = new ReadResult(result.Buffer.Slice(0, this.RemainingLength), isCanceled: false, isCompleted: true);
            }

            return this.resultOfPriorRead = result;
        }

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result)
        {
            Verify.Operation(!this.readerCompleted, Strings.ReadingAfterCompletionNotAllowed);
            if (this.resultOfPriorRead.IsCompleted)
            {
                result = this.resultOfPriorRead = new ReadResult(this.resultOfPriorRead.Buffer, isCanceled: this.nextReadCanceled, isCompleted: true);
                this.nextReadCanceled = false;
                return true;
            }

            if (this.pipeReader.TryRead(out result))
            {
                if (result.Buffer.Length > this.RemainingLength)
                {
                    result = new ReadResult(result.Buffer.Slice(0, this.RemainingLength), isCanceled: false, isCompleted: true);
                }

                this.resultOfPriorRead = result;
                return true;
            }

            return false;
        }

        private bool Consumed(SequencePosition consumed, SequencePosition examined)
        {
            this.consumedLength += this.resultOfPriorRead.Buffer.Slice(0, consumed).Length;
            this.examinedLength += this.resultOfPriorRead.Buffer.Slice(0, examined).Length;

            if (this.resultOfPriorRead.IsCompleted)
            {
                // We have to keep our own ReadResult current since we can't call the inner read methods any more.
                this.resultOfPriorRead = new ReadResult(this.resultOfPriorRead.Buffer.Slice(consumed), isCanceled: false, isCompleted: true);
            }

            // The caller is allowed to propagate the AdvanceTo call to the underlying reader iff
            // we will be reading again from it, or our own final buffer is empty.
            return !this.resultOfPriorRead.IsCompleted || this.resultOfPriorRead.Buffer.IsEmpty;
        }
    }
}
