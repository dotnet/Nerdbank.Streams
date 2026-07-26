// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System;
    using System.IO.Pipelines;
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;

    /// <summary>
    /// Measures aggregate throughput when several channels transmit at once.
    /// </summary>
    /// <remarks>
    /// All frames on a connection are serialized through a single send path, so this benchmark is the
    /// one that exposes contention there. A change that helps a single channel but serializes poorly
    /// across many will show up here and nowhere else.
    /// </remarks>
    public class ContendedChannelsBenchmark : MultiplexingStreamBenchmarkBase
    {
        private const int ChunkSize = 64 * 1024;

        private static readonly byte[] Chunk = new byte[ChunkSize];

        private int roundCounter;

        /// <summary>
        /// Gets or sets the number of channels transmitting concurrently.
        /// </summary>
        [Params(8)]
        public int ChannelCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of bytes to transmit across all channels in each operation.
        /// </summary>
        [Params(32 * 1024 * 1024)]
        public int TotalTransferSize { get; set; }

        /// <summary>
        /// Transmits <see cref="TotalTransferSize"/> bytes divided evenly across <see cref="ChannelCount"/> channels.
        /// </summary>
        /// <returns>A task that tracks the transfers.</returns>
        [Benchmark]
        public async Task TransmitOverManyChannels()
        {
            int round = this.roundCounter++;
            var channels = new (MultiplexingStream.Channel Sender, MultiplexingStream.Channel Receiver)[this.ChannelCount];
            for (int i = 0; i < this.ChannelCount; i++)
            {
                channels[i] = await this.CreateChannelAsync($"contended{round}-{i}");
            }

            try
            {
                long perChannel = this.TotalTransferSize / this.ChannelCount;
                var tasks = new Task[this.ChannelCount * 2];
                for (int i = 0; i < this.ChannelCount; i++)
                {
                    (MultiplexingStream.Channel Sender, MultiplexingStream.Channel Receiver) channel = channels[i];
                    tasks[i] = Task.Run(async delegate
                    {
                        long bytesReceived = 0;
                        while (bytesReceived < perChannel)
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
                    tasks[this.ChannelCount + i] = Task.Run(async delegate
                    {
                        for (long bytesSent = 0; bytesSent < perChannel;)
                        {
                            int bytesThisRound = (int)Math.Min(ChunkSize, perChannel - bytesSent);
                            Chunk.AsSpan(0, bytesThisRound).CopyTo(channel.Sender.Output.GetSpan(bytesThisRound));
                            channel.Sender.Output.Advance(bytesThisRound);
                            await channel.Sender.Output.FlushAsync();
                            bytesSent += bytesThisRound;
                        }
                    });
                }

                await Task.WhenAll(tasks);
            }
            finally
            {
                foreach ((MultiplexingStream.Channel Sender, MultiplexingStream.Channel Receiver) channel in channels)
                {
                    channel.Sender.Dispose();
                    channel.Receiver.Dispose();
                }
            }
        }
    }
}
