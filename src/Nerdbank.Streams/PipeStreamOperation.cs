// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !SPAN_BUILTIN

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.IO.Pipes;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Sources;
    using Microsoft;

    internal sealed class PipeStreamOperation : IValueTaskSource<int>, IValueTaskSource
    {
        private static readonly AsyncCallback Callback = OnCompleted;

        private ManualResetValueTaskSourceCore<int> completionSource = new() { RunContinuationsAsynchronously = true };
        private Stream? stream;
        private byte[]? rentedBuffer;
        private Memory<byte> readDestination;
        private bool reading;
        private bool operationPending;

        public int GetResult(short token) => this.completionSource.GetResult(token);

        void IValueTaskSource.GetResult(short token) => this.completionSource.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => this.completionSource.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => this.completionSource.OnCompleted(continuation, state, token, flags);

        internal ValueTask<int> ReadAsync(PipeStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            Requires.NotNull(stream, nameof(stream));

            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
            }

            this.Prepare(stream, reading: true);
            short version = this.completionSource.Version;
            try
            {
                ArraySegment<byte> readBuffer;
                if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> bufferArray) && bufferArray.Array is not null)
                {
                    readBuffer = bufferArray;
                }
                else
                {
                    this.rentedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                    this.readDestination = buffer;
                    readBuffer = new ArraySegment<byte>(this.rentedBuffer, 0, buffer.Length);
                }

                IAsyncResult asyncResult = stream.BeginRead(readBuffer.Array!, readBuffer.Offset, readBuffer.Count, Callback, this);
                if (asyncResult.CompletedSynchronously)
                {
                    this.Complete(asyncResult);
                }
            }
            catch
            {
                this.Cleanup();
                throw;
            }

            return new ValueTask<int>(this, version);
        }

        internal ValueTask WriteAsync(PipeStream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            Requires.NotNull(stream, nameof(stream));

            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask(Task.FromCanceled(cancellationToken));
            }

            this.Prepare(stream, reading: false);
            short version = this.completionSource.Version;
            try
            {
                ArraySegment<byte> writeBuffer;
                if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> bufferArray) && bufferArray.Array is not null)
                {
                    writeBuffer = bufferArray;
                }
                else
                {
                    this.rentedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                    buffer.Span.CopyTo(this.rentedBuffer);
                    writeBuffer = new ArraySegment<byte>(this.rentedBuffer, 0, buffer.Length);
                }

                IAsyncResult asyncResult = stream.BeginWrite(writeBuffer.Array!, writeBuffer.Offset, writeBuffer.Count, Callback, this);
                if (asyncResult.CompletedSynchronously)
                {
                    this.Complete(asyncResult);
                }
            }
            catch
            {
                this.Cleanup();
                throw;
            }

            return new ValueTask(this, version);
        }

        private static void OnCompleted(IAsyncResult asyncResult)
        {
            if (!asyncResult.CompletedSynchronously)
            {
                ((PipeStreamOperation)asyncResult.AsyncState!).Complete(asyncResult);
            }
        }

        private void Prepare(Stream stream, bool reading)
        {
            Verify.Operation(!this.operationPending, "Only one stream operation may be pending at a time.");
            this.completionSource.Reset();
            this.stream = stream;
            this.reading = reading;
            this.operationPending = true;
        }

        private void Complete(IAsyncResult asyncResult)
        {
            try
            {
                Stream? stream = this.stream;
                Assumes.NotNull(stream);
                int result = 0;
                if (this.reading)
                {
                    result = stream.EndRead(asyncResult);
                    if (this.rentedBuffer is not null)
                    {
                        new ReadOnlySpan<byte>(this.rentedBuffer, 0, result).CopyTo(this.readDestination.Span);
                    }
                }
                else
                {
                    stream.EndWrite(asyncResult);
                }

                this.Cleanup();
                this.completionSource.SetResult(result);
            }
            catch (Exception ex)
            {
                this.Cleanup();
                this.completionSource.SetException(ex);
            }
        }

        private void Cleanup()
        {
            byte[]? rentedBuffer = this.rentedBuffer;
            this.rentedBuffer = null;
            this.readDestination = default;
            this.stream = null;
            this.operationPending = false;
            if (rentedBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }
}

#endif
