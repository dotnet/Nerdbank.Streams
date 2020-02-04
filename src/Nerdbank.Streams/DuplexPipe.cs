// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A basic implementation of <see cref="IDuplexPipe"/>.
    /// </summary>
    public sealed class DuplexPipe : IDuplexPipe
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DuplexPipe"/> class.
        /// </summary>
        /// <param name="input">The reader. If null, a completed reader will be emulated.</param>
        /// <param name="output">The writer. If null, a completed writer will be emulated.</param>
        public DuplexPipe(PipeReader? input, PipeWriter? output)
        {
            this.Input = input ?? CompletedPipeReader.Singleton;
            this.Output = output ?? CompletedPipeWriter.Singleton;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DuplexPipe"/> class
        /// that only allows reading. The <see cref="Output"/> property will reject writes.
        /// </summary>
        /// <param name="input">The reader.</param>
        public DuplexPipe(PipeReader input)
            : this(input, output: null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DuplexPipe"/> class
        /// that only allows writing. The <see cref="Input"/> property will report completed reading.
        /// </summary>
        /// <param name="output">The writer.</param>
        public DuplexPipe(PipeWriter output)
            : this(input: null, output)
        {
        }

        /// <inheritdoc />
        public PipeReader Input { get; }

        /// <inheritdoc />
        public PipeWriter Output { get; }

        private class CompletedPipeWriter : PipeWriter
        {
            internal static readonly PipeWriter Singleton = new CompletedPipeWriter();

            private CompletedPipeWriter()
            {
            }

            public override void Advance(int bytes)
            {
                if (bytes > 0)
                {
                    ThrowInvalidOperationException();
                }
            }

            public override void CancelPendingFlush()
            {
            }

            public override void Complete(Exception? exception)
            {
            }

            public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) => default;

            public override Memory<byte> GetMemory(int sizeHint) => throw ThrowInvalidOperationException();

            public override Span<byte> GetSpan(int sizeHint) => throw ThrowInvalidOperationException();

            [Obsolete]
            public override void OnReaderCompleted(Action<Exception, object> callback, object state)
            {
            }

            private static Exception ThrowInvalidOperationException() => throw new InvalidOperationException("Writing is completed.");
        }

        private class CompletedPipeReader : PipeReader
        {
            internal static readonly PipeReader Singleton = new CompletedPipeReader();

            private CompletedPipeReader()
            {
            }

            public override void AdvanceTo(SequencePosition consumed) => ThrowInvalidOperationException();

            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => ThrowInvalidOperationException();

            public override void CancelPendingRead()
            {
            }

            public override void Complete(Exception? exception)
            {
            }

            [Obsolete]
            public override void OnWriterCompleted(Action<Exception, object> callback, object state)
            {
            }

            public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken) => throw ThrowInvalidOperationException();

            public override bool TryRead(out ReadResult result) => throw ThrowInvalidOperationException();

            private static Exception ThrowInvalidOperationException() => throw new InvalidOperationException("Reading is completed.");
        }
    }
}
