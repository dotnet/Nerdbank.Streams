// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;

    /// <summary>
    /// Measures bulk transfer over a transport with artificial latency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other benchmarks all run over loopback, where a round trip is essentially free. That makes
    /// them blind to the central trade-off in the receive window's flow control: returning credit less
    /// often costs fewer frames but makes the sender wait a full round trip when it does run out.
    /// On loopback the second half of that trade-off is invisible, so tuning against loopback alone
    /// will happily pick a setting that behaves badly on a real network.
    /// </para>
    /// <para>
    /// Throughput here is expected to approach the window divided by the round trip time whenever the
    /// window is the binding constraint, so the interesting signal is how much larger a window must be
    /// as latency grows.
    /// </para>
    /// </remarks>
    public class LatencyBulkTransferBenchmark : MultiplexingStreamBenchmarkBase
    {
        private byte[] payload = null!;

        /// <summary>
        /// Gets or sets the one-way delay applied to the transport, in milliseconds.
        /// </summary>
        /// <remarks>
        /// The round trip time is twice this. Zero disables the wrapper entirely, giving a
        /// direct comparison against the plain loopback benchmarks. The platform timer cannot
        /// faithfully reproduce delays below about a millisecond, so no smaller value is offered.
        /// </remarks>
        [Params(0, 1, 8)]
        public int OneWayLatencyMs { get; set; }

        /// <summary>
        /// Gets or sets the receiving window size to configure, or 0 to use the default.
        /// </summary>
        [Params(0, 4 * 1024 * 1024)]
        public int WindowSize { get; set; }

        /// <summary>
        /// Gets or sets the number of bytes to transfer.
        /// </summary>
        /// <remarks>
        /// This is much smaller than the loopback benchmarks use, because a latent connection
        /// takes far longer to move the same volume.
        /// </remarks>
        [Params(4 * 1024 * 1024)]
        public int TransferSize { get; set; }

        /// <summary>
        /// Transfers <see cref="TransferSize"/> bytes over a single channel.
        /// </summary>
        /// <returns>A task that tracks the transfer.</returns>
        [Benchmark]
        public async Task TransmitBulkData()
        {
            (MultiplexingStream.Channel sender, MultiplexingStream.Channel receiver) = await this.CreateChannelAsync(Guid.NewGuid().ToString("n"));

            Task writeTask = Task.Run(async delegate
            {
                await sender.Output.WriteAsync(this.payload);
                await sender.Output.CompleteAsync();
            });

            long bytesRead = 0;
            while (bytesRead < this.TransferSize)
            {
                ReadResult readResult = await receiver.Input.ReadAsync();
                if (readResult.Buffer.IsEmpty && readResult.IsCompleted)
                {
                    break;
                }

                bytesRead += readResult.Buffer.Length;
                receiver.Input.AdvanceTo(readResult.Buffer.End);
            }

            await writeTask;
            sender.Dispose();
            receiver.Dispose();
        }

        /// <inheritdoc/>
        protected override MultiplexingStream.Options CreateOptions()
        {
            MultiplexingStream.Options options = base.CreateOptions();
            if (this.WindowSize > 0)
            {
                options.DefaultChannelReceivingWindowSize = this.WindowSize;
            }

            return options;
        }

        /// <inheritdoc/>
        protected override Stream WrapTransport(Stream transport)
            => this.OneWayLatencyMs == 0 ? transport : new LatencyStream(transport, TimeSpan.FromMilliseconds(this.OneWayLatencyMs));

        /// <inheritdoc/>
        protected override Task OnConnectedAsync()
        {
            this.payload = new byte[this.TransferSize];
            return Task.CompletedTask;
        }
    }
}
