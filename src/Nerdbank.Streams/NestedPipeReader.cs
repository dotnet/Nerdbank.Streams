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
        private ReadResult resultOfPriorRead;
        private bool completed;

        public NestedPipeReader(PipeReader pipeReader, long length)
        {
            Requires.NotNull(pipeReader, nameof(pipeReader));
            Requires.Range(length >= 0, nameof(length));

            this.pipeReader = pipeReader;
            this.length = length;
        }

        private long RemainingLength => this.length - this.consumedLength;

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed) => this.AdvanceTo(consumed, consumed);

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            this.consumedLength += this.resultOfPriorRead.Buffer.Slice(0, consumed).Length;
            this.pipeReader.AdvanceTo(consumed, examined);

            // When we call AdvanceTo on the underlying reader, we're not allowed to reference their buffer any more, so clear it to be safe.
            this.resultOfPriorRead = new ReadResult(default, isCanceled: false, isCompleted: this.resultOfPriorRead.IsCompleted);
        }

        /// <inheritdoc/>
        public override void CancelPendingRead() => this.pipeReader.CancelPendingRead();

        /// <inheritdoc/>
        /// <remarks>
        /// If the slice has not been fully read or if <paramref name="exception"/> is non-null, this call propagates to the underlying <see cref="PipeReader"/>.
        /// But if the slice is fully read without errors, the call is suppressed so the underlying <see cref="PipeReader"/> can continue to function.
        /// </remarks>
        public override void Complete(Exception? exception = null)
        {
            this.completed = true;
            if (exception is object || this.RemainingLength > 0)
            {
                this.pipeReader.Complete(exception);
            }
        }

        /// <inheritdoc/>
        public override void OnWriterCompleted(Action<Exception, object> callback, object? state)
        {
            // We will never call this back. The method is deprecated in .NET Core 3.0 anyway.
        }

        /// <inheritdoc/>
        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            Verify.Operation(!this.completed, Strings.ReadingAfterCompletionNotAllowed);

            ReadResult result;
            if (this.RemainingLength == 0)
            {
                // We do NOT want to block on reading more bytes since we don't expect or want any.
                // But we DO want to put the underlying reader back into a reading mode so we don't
                // have to emulate that in our class.
                this.pipeReader.TryRead(out result);
            }
            else
            {
                result = await this.pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            // Do not allow the reader to exceed the length of this slice.
            if (result.Buffer.Length >= this.RemainingLength)
            {
                result = new ReadResult(result.Buffer.Slice(0, this.RemainingLength), isCanceled: result.IsCanceled, isCompleted: true);
            }

            return this.resultOfPriorRead = result;
        }

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result)
        {
            Verify.Operation(!this.completed, Strings.ReadingAfterCompletionNotAllowed);
            if (this.pipeReader.TryRead(out result))
            {
                // Do not allow the reader to exceed the length of this slice.
                if (result.Buffer.Length > this.RemainingLength)
                {
                    result = new ReadResult(result.Buffer.Slice(0, this.RemainingLength), isCanceled: result.IsCanceled, isCompleted: true);
                }

                this.resultOfPriorRead = result;
                return true;
            }

            return false;
        }
    }
}
