// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Wraps a stream so that data becomes readable only after a fixed one-way delay,
    /// simulating a network with meaningful latency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Writes pass straight through; the delay is applied on the reading side. Wrapping both ends
    /// of a connection therefore delays each direction once, producing a round trip time of twice
    /// the configured one-way delay.
    /// </para>
    /// <para>
    /// Delayed data is held in a queue and released by a background pump, so transfers remain
    /// pipelined: throughput is limited by the sender and the flow control window rather than by
    /// the delay applied to any single chunk. Simply awaiting before returning from each read
    /// would instead serialize the connection and cap throughput at one chunk per delay.
    /// </para>
    /// <para>
    /// Accuracy is bounded by the platform timer, so delays below about a millisecond are not
    /// faithfully reproduced. This class is meant for comparing configurations at the same
    /// configured latency, not for predicting absolute throughput on a real network.
    /// </para>
    /// </remarks>
    internal sealed class LatencyStream : Stream
    {
        private readonly Stream inner;
        private readonly long delayTicks;
        private readonly Pipe delayed = new Pipe();
        private readonly Stream delayedReader;
        private readonly ConcurrentQueue<(long DueTimestamp, byte[] Buffer, int Length)> queue = new ConcurrentQueue<(long, byte[], int)>();
        private readonly SemaphoreSlim queueSignal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource disposalSource = new CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of the <see cref="LatencyStream"/> class.
        /// </summary>
        /// <param name="inner">The underlying transport.</param>
        /// <param name="oneWayDelay">How long to withhold data that arrives on <paramref name="inner"/>.</param>
        internal LatencyStream(Stream inner, TimeSpan oneWayDelay)
        {
            this.inner = inner;
            this.delayTicks = (long)(oneWayDelay.TotalSeconds * Stopwatch.Frequency);
            this.delayedReader = this.delayed.Reader.AsStream();

            Task.Run(this.ReceivePumpAsync);
            Task.Run(this.ReleasePumpAsync);
        }

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush() => this.inner.Flush();

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => this.inner.FlushAsync(cancellationToken);

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => this.delayedReader.Read(buffer, offset, count);

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => this.delayedReader.ReadAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => this.inner.Write(buffer, offset, count);

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => this.inner.WriteAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.disposalSource.Cancel();
                this.inner.Dispose();
                this.delayedReader.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Waits until the given timestamp has passed, sleeping when the remaining time exceeds
        /// the timer's useful resolution and yielding otherwise.
        /// </summary>
        /// <param name="dueTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value to wait for.</param>
        /// <returns>A task that completes once the timestamp has passed.</returns>
        private static async Task WaitUntilAsync(long dueTimestamp)
        {
            while (true)
            {
                double remainingMs = (dueTimestamp - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
                if (remainingMs <= 0)
                {
                    return;
                }

                if (remainingMs > 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(remainingMs - 1)).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }

        /// <summary>
        /// Continuously moves data off the transport and stamps it with the time it may be released,
        /// so that reading is never blocked behind an earlier chunk's delay.
        /// </summary>
        /// <returns>A task that completes when the transport is exhausted.</returns>
        private async Task ReceivePumpAsync()
        {
            try
            {
                while (!this.disposalSource.IsCancellationRequested)
                {
                    // Each chunk must survive until its release time, so it cannot share one buffer.
                    // Pooling keeps this harness from allocating the entire transfer volume, which
                    // would otherwise show up as GC pressure in the measurements it is meant to inform.
                    byte[] chunk = ArrayPool<byte>.Shared.Rent(64 * 1024);
                    int bytesRead = await this.inner.ReadAsync(chunk, 0, chunk.Length, this.disposalSource.Token).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        ArrayPool<byte>.Shared.Return(chunk);
                        break;
                    }

                    this.queue.Enqueue((Stopwatch.GetTimestamp() + this.delayTicks, chunk, bytesRead));
                    this.queueSignal.Release();
                }
            }
            catch (Exception ex)
            {
                this.delayed.Writer.Complete(ex);
                return;
            }

            this.delayed.Writer.Complete();
        }

        /// <summary>
        /// Releases queued data to the reader once each chunk's delay has elapsed.
        /// </summary>
        /// <returns>A task that completes when the stream is disposed.</returns>
        private async Task ReleasePumpAsync()
        {
            try
            {
                while (!this.disposalSource.IsCancellationRequested)
                {
                    await this.queueSignal.WaitAsync(this.disposalSource.Token).ConfigureAwait(false);
                    if (!this.queue.TryDequeue(out (long DueTimestamp, byte[] Buffer, int Length) item))
                    {
                        continue;
                    }

                    await WaitUntilAsync(item.DueTimestamp).ConfigureAwait(false);
                    await this.delayed.Writer.WriteAsync(new ReadOnlyMemory<byte>(item.Buffer, 0, item.Length), this.disposalSource.Token).ConfigureAwait(false);
                    ArrayPool<byte>.Shared.Return(item.Buffer);
                }
            }
            catch (OperationCanceledException)
            {
                // Disposed.
            }
            catch (Exception ex)
            {
                this.delayed.Writer.Complete(ex);
            }
        }
    }
}
