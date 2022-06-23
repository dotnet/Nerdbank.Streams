// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// A substitute <see cref="PipeReader"/> that throws for nearly every call, notifying the caller that they don't own this object.
    /// The only allowed call is to <see cref="PipeReader.CancelPendingRead"/> because that is assumed to be thread-safe.
    /// </summary>
    /// <remarks>
    /// This can be useful to set on a field that contains a <see cref="PipeReader"/> while some async method "owns" it.
    /// One example is at disposal, the 'owning' object may be tempted to call <see cref="PipeReader.Complete"/> on it,
    /// but this can cause thread-safety bugs and data corruption when the async reader loop is still using it.
    /// Instead, such a Dispose method should cancel the reader loop in a thread-safe fashion, and then either that read method
    /// or the Dispose method can Complete the reader.
    /// </remarks>
    internal class UnownedPipeReader : PipeReader
    {
        internal const string UnownedObject = "This object is owned by another context and should not be accessed from another.";
        private readonly PipeReader underlyingReader;

        internal UnownedPipeReader(PipeReader underlyingReader)
        {
            this.underlyingReader = underlyingReader;
        }

        public override void CancelPendingRead() => this.underlyingReader.CancelPendingRead();

        public override void AdvanceTo(SequencePosition consumed) => throw Assumes.Fail(UnownedObject);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => throw Assumes.Fail(UnownedObject);

        public override void Complete(Exception? exception = null) => throw Assumes.Fail(UnownedObject);

        public override ValueTask CompleteAsync(Exception? exception = null) => throw Assumes.Fail(UnownedObject);

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) => throw Assumes.Fail(UnownedObject);

        public override bool TryRead(out ReadResult result) => throw Assumes.Fail(UnownedObject);
    }
}
