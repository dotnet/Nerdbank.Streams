// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;

    /// <summary>
    /// Measures the cost of opening and closing many channels that carry little or no data.
    /// </summary>
    /// <remarks>
    /// This is the shape of a large IDE session, where most channels exist to carry occasional small
    /// messages rather than bulk data. Paired with the memory diagnoser, it is the guard against
    /// changes that buy throughput by making every channel more expensive to own.
    /// </remarks>
    public class ChannelLifetimeBenchmark : MultiplexingStreamBenchmarkBase
    {
        private int roundCounter;

        /// <summary>
        /// Gets or sets the number of channels to open and close in each operation.
        /// </summary>
        [Params(500)]
        public int ChannelCount { get; set; }

        /// <summary>
        /// Opens <see cref="ChannelCount"/> channels, then closes them all.
        /// </summary>
        /// <returns>A task that tracks the work.</returns>
        [Benchmark]
        public async Task OpenAndCloseChannels()
        {
            int round = this.roundCounter++;
            var channels = new (MultiplexingStream.Channel Sender, MultiplexingStream.Channel Receiver)[this.ChannelCount];
            for (int i = 0; i < this.ChannelCount; i++)
            {
                channels[i] = await this.CreateChannelAsync($"lifetime{round}-{i}");
            }

            foreach ((MultiplexingStream.Channel Sender, MultiplexingStream.Channel Receiver) channel in channels)
            {
                channel.Sender.Dispose();
                channel.Receiver.Dispose();
            }
        }
    }
}
