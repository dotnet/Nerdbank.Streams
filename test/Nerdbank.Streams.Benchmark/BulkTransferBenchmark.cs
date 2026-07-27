// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System;
    using System.IO.Pipelines;
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;

    /// <summary>
    /// Measures throughput when one channel carries a large, one-way payload,
    /// which is the "large file transfer" scenario reported in
    /// <see href="https://github.com/dotnet/Nerdbank.Streams/issues/505">issue #505</see>.
    /// </summary>
    /// <remarks>
    /// This scenario is dominated by flow control: the sender must stop and wait whenever it has
    /// filled the receiver's window, so it is the most sensitive benchmark to window sizing and to
    /// how often the receiver acknowledges the data it has consumed.
    /// </remarks>
    public class BulkTransferBenchmark : MultiplexingStreamBenchmarkBase
    {
        private const int ChunkSize = 64 * 1024;

        private static readonly byte[] Chunk = new byte[ChunkSize];

        private int channelCounter;

        /// <summary>
        /// Gets or sets the number of bytes to transmit in each operation.
        /// </summary>
        [Params(32 * 1024 * 1024)]
        public int TransferSize { get; set; }

        /// <summary>
        /// Gets or sets the receiving window size to configure for each channel,
        /// or 0 to use the default.
        /// </summary>
        /// <remarks>
        /// A deliberately small window is included because it stresses the acknowledgement path far
        /// harder than the default does, and is therefore the case most likely to regress when the
        /// acknowledgement policy changes.
        /// </remarks>
        [Params(0, 100 * 1024)]
        public int WindowSize { get; set; }

        /// <summary>
        /// Transmits <see cref="TransferSize"/> bytes across a fresh channel and waits for it all to arrive.
        /// </summary>
        /// <returns>A task that tracks the transfer.</returns>
        [Benchmark]
        public async Task TransmitBulkData()
        {
            (MultiplexingStream.Channel Sender, MultiplexingStream.Channel Receiver) channel =
                await this.CreateChannelAsync($"bulk{this.channelCounter++}");
            try
            {
                long transferSize = this.TransferSize;
                Task receiveTask = Task.Run(async delegate
                {
                    long bytesReceived = 0;
                    while (bytesReceived < transferSize)
                    {
                        ReadResult readResult = await channel.Receiver.Input.ReadAsync();
                        if (readResult.Buffer.IsEmpty && readResult.IsCompleted)
                        {
                            break;
                        }

                        bytesReceived += readResult.Buffer.Length;
                        channel.Receiver.Input.AdvanceTo(readResult.Buffer.End);
                    }
                });

                for (long bytesSent = 0; bytesSent < transferSize;)
                {
                    int bytesThisRound = (int)Math.Min(ChunkSize, transferSize - bytesSent);
                    Chunk.AsSpan(0, bytesThisRound).CopyTo(channel.Sender.Output.GetSpan(bytesThisRound));
                    channel.Sender.Output.Advance(bytesThisRound);
                    await channel.Sender.Output.FlushAsync();
                    bytesSent += bytesThisRound;
                }

                await receiveTask;
            }
            finally
            {
                channel.Sender.Dispose();
                channel.Receiver.Dispose();
            }
        }

        /// <inheritdoc/>
        protected override MultiplexingStream.Options CreateOptions()
        {
            MultiplexingStream.Options options = base.CreateOptions();

            // Version 1 has no backpressure, so it has no window to configure.
            if (this.WindowSize > 0 && this.ProtocolMajorVersion > 1)
            {
                options.DefaultChannelReceivingWindowSize = this.WindowSize;
            }

            return options;
        }
    }
}
