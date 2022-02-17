// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;

    internal class PipeWriterCompletionWatcher : PipeWriter
    {
        private readonly PipeWriter inner;
        private Action<Exception?, object?>? callback;
        private object? state;

        public PipeWriterCompletionWatcher(PipeWriter inner, Action<Exception?, object?> callback, object? state)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
            this.state = state;
        }

        /// <inheritdoc/>
        public override void Advance(int bytes) => this.inner.Advance(bytes);

        /// <inheritdoc/>
        public override void CancelPendingFlush() => this.inner.CancelPendingFlush();

        /// <inheritdoc/>
        public override void Complete(Exception? exception = null)
        {
            this.inner.Complete(exception);
            Action<Exception?, object?>? callback = Interlocked.Exchange(ref this.callback, null);
            callback?.Invoke(exception, this.state);
            this.state = null;
        }

        /// <inheritdoc/>
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) => this.inner.FlushAsync(cancellationToken);

        /// <inheritdoc/>
        public override Memory<byte> GetMemory(int sizeHint = 0) => this.inner.GetMemory(sizeHint);

        /// <inheritdoc/>
        public override Span<byte> GetSpan(int sizeHint = 0) => this.inner.GetSpan(sizeHint);

        /// <inheritdoc/>
        [Obsolete]
        public override void OnReaderCompleted(Action<Exception?, object?> callback, object? state) => this.inner.OnReaderCompleted(callback, state);
    }
}
