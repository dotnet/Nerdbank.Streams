// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

#pragma warning disable AvoidAsyncSuffix // Avoid Async suffix (malfunctions on ValueTask)

namespace Nerdbank.Streams
{
    using System;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// Provides a full duplex stream which may be shared by two parties to
    /// exchange messages.
    /// </summary>
    public static class FullDuplexStream
    {
        /// <summary>
        /// Creates a pair of streams that can be passed to two parties
        /// to allow for interaction with each other.
        /// </summary>
        /// <param name="pipeOptions">Pipe options to initialize the internal pipes with.</param>
        /// <returns>A pair of streams.</returns>
        public static (Stream, Stream) CreatePair(PipeOptions pipeOptions = null)
        {
            var (pipe1, pipe2) = CreatePipePair(pipeOptions);
            return (pipe1.AsStream(), pipe2.AsStream());
        }

        /// <summary>
        /// Creates a pair of duplex pipes that can be passed to two parties
        /// to allow for interaction with each other.
        /// </summary>
        /// <param name="pipeOptions">Pipe options to initialize the internal pipes with.</param>
        /// <returns>A pair of <see cref="IDuplexPipe"/> objects.</returns>
        public static (IDuplexPipe, IDuplexPipe) CreatePipePair(PipeOptions pipeOptions = null)
        {
            var pipe1 = new Pipe(pipeOptions ?? PipeOptions.Default);
            var pipe2 = new Pipe(pipeOptions ?? PipeOptions.Default);

            var party1 = new DuplexPipe(pipe1.Reader, pipe2.Writer);
            var party2 = new DuplexPipe(pipe2.Reader, pipe1.Writer);

            return (party1, party2);
        }

        /// <summary>
        /// Combines a readable <see cref="Stream"/> with a writable <see cref="Stream"/> into a new full-duplex <see cref="Stream"/>
        /// that reads and writes to the specified streams.
        /// </summary>
        /// <param name="readableStream">A readable stream.</param>
        /// <param name="writableStream">A writable stream.</param>
        /// <returns>A new full-duplex stream.</returns>
        public static Stream Splice(Stream readableStream, Stream writableStream) => new CombinedStream(readableStream, writableStream);

        private class CombinedStream : Stream, IDisposableObservable
        {
            private readonly Stream readableStream;
            private readonly Stream writableStream;

            internal CombinedStream(Stream readableStream, Stream writableStream)
            {
                Requires.NotNull(readableStream, nameof(readableStream));
                Requires.NotNull(writableStream, nameof(writableStream));

                Requires.Argument(readableStream.CanRead, nameof(readableStream), "Must be readable");
                Requires.Argument(writableStream.CanWrite, nameof(writableStream), "Must be writable");

                this.readableStream = readableStream;
                this.writableStream = writableStream;
            }

            public override bool CanRead => !this.IsDisposed;

            public override bool CanSeek => false;

            public override bool CanWrite => !this.IsDisposed;

            public override bool CanTimeout => this.readableStream.CanTimeout || this.writableStream.CanTimeout;

            public override int ReadTimeout
            {
                get => this.readableStream.ReadTimeout;
                set => this.readableStream.ReadTimeout = value;
            }

            public override int WriteTimeout
            {
                get => this.writableStream.WriteTimeout;
                set => this.writableStream.WriteTimeout = value;
            }

            public override long Length => throw this.ThrowDisposedOr(new NotSupportedException());

            public override long Position
            {
                get => throw this.ThrowDisposedOr(new NotSupportedException());
                set => throw this.ThrowDisposedOr(new NotSupportedException());
            }

            public bool IsDisposed { get; private set; }

            public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

            public override void SetLength(long value) => this.ThrowDisposedOr(new NotSupportedException());

            public override void Flush() => this.writableStream.Flush();

            public override Task FlushAsync(CancellationToken cancellationToken) => this.writableStream.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) => this.readableStream.Read(buffer, offset, count);

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => this.readableStream.ReadAsync(buffer, offset, count, cancellationToken);

            public override int ReadByte() => this.readableStream.ReadByte();

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => this.readableStream.CopyToAsync(destination, bufferSize, cancellationToken);

            public override void Write(byte[] buffer, int offset, int count) => this.writableStream.Write(buffer, offset, count);

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => this.writableStream.WriteAsync(buffer, offset, count, cancellationToken);

            public override void WriteByte(byte value) => this.writableStream.WriteByte(value);

#if SPAN_BUILTIN

            public override void Write(ReadOnlySpan<byte> buffer) => this.writableStream.Write(buffer);

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => this.writableStream.WriteAsync(buffer, cancellationToken);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => this.readableStream.ReadAsync(buffer, cancellationToken);

            public override int Read(Span<byte> buffer) => this.readableStream.Read(buffer);

#endif

            protected override void Dispose(bool disposing)
            {
                this.IsDisposed = true;
                if (disposing)
                {
                    this.readableStream.Dispose();
                    this.writableStream.Dispose();
                }

                base.Dispose(disposing);
            }

            private Exception ThrowDisposedOr(Exception ex)
            {
                Verify.NotDisposed(this);
                throw ex;
            }
        }
    }
}
