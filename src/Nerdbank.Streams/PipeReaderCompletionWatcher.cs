// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;

    internal class PipeReaderCompletionWatcher : PipeReader
    {
        private readonly PipeReader inner;
        private Action<Exception?, object?>? callback;
        private object? state;

        internal PipeReaderCompletionWatcher(PipeReader inner, Action<Exception?, object?> callback, object? state)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
            this.state = state;
        }

        public override void AdvanceTo(SequencePosition consumed) => this.inner.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => this.inner.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => this.inner.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            this.inner.Complete(exception);
            var callback = Interlocked.Exchange(ref this.callback, null);
            callback?.Invoke(exception, this.state);
            this.state = null;
        }

        [Obsolete]
        public override void OnWriterCompleted(Action<Exception?, object?> callback, object? state) => this.inner.OnWriterCompleted(callback, state);

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) => this.inner.ReadAsync(cancellationToken);

        public override bool TryRead(out ReadResult result) => this.inner.TryRead(out result);
    }
}
