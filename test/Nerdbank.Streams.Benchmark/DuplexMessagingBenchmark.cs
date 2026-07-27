// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System.IO.Pipelines;
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;

    /// <summary>
    /// Measures round-trip latency for small messages, which is representative of an RPC workload
    /// such as the one StreamJsonRpc layers on top of this library.
    /// </summary>
    /// <remarks>
    /// Messages here are far too small to ever fill a channel's window, so this benchmark is
    /// deliberately insensitive to window sizing. Its purpose is to guard the latency of a single
    /// frame's journey through the send path, which bulk throughput benchmarks hide.
    /// </remarks>
    public class DuplexMessagingBenchmark : MultiplexingStreamBenchmarkBase
    {
        private const int MessageSize = 128;

        private MultiplexingStream.Channel? requester;
        private MultiplexingStream.Channel? responder;
        private Task? echoTask;

        /// <summary>
        /// Gets or sets the number of round trips to perform in each operation.
        /// </summary>
        [Params(1000)]
        public int RoundTrips { get; set; }

        /// <summary>
        /// Sends a small message and waits for it to be echoed back, <see cref="RoundTrips"/> times.
        /// </summary>
        /// <returns>A task that tracks the round trips.</returns>
        [Benchmark]
        public async Task RoundTripSmallMessages()
        {
            for (int i = 0; i < this.RoundTrips; i++)
            {
                this.requester!.Output.GetSpan(MessageSize).Slice(0, MessageSize).Clear();
                this.requester.Output.Advance(MessageSize);
                await this.requester.Output.FlushAsync();

                int bytesRead = 0;
                while (bytesRead < MessageSize)
                {
                    ReadResult readResult = await this.requester.Input.ReadAsync();
                    if (readResult.Buffer.IsEmpty && readResult.IsCompleted)
                    {
                        return;
                    }

                    bytesRead += (int)readResult.Buffer.Length;
                    this.requester.Input.AdvanceTo(readResult.Buffer.End);
                }
            }
        }

        /// <summary>
        /// Opens the channel used for all iterations and starts the echo loop on the far end.
        /// </summary>
        /// <returns>A task that tracks the setup.</returns>
        protected override async Task OnConnectedAsync()
        {
            (MultiplexingStream.Channel Sender, MultiplexingStream.Channel Receiver) channel = await this.CreateChannelAsync("duplex");
            this.requester = channel.Sender;
            this.responder = channel.Receiver;

            // Echo everything back for as long as the channel is open.
            this.echoTask = Task.Run(async delegate
            {
                while (true)
                {
                    ReadResult readResult = await this.responder.Input.ReadAsync();
                    if (readResult.Buffer.IsEmpty && readResult.IsCompleted)
                    {
                        return;
                    }

                    foreach (System.ReadOnlyMemory<byte> segment in readResult.Buffer)
                    {
                        segment.Span.CopyTo(this.responder.Output.GetSpan(segment.Length));
                        this.responder.Output.Advance(segment.Length);
                    }

                    this.responder.Input.AdvanceTo(readResult.Buffer.End);
                    await this.responder.Output.FlushAsync();
                }
            });
        }
    }
}
