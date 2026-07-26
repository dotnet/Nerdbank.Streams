// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System;
    using System.IO;
    using System.IO.Pipelines;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;

    /// <summary>
    /// Measures the throughput of a single <see cref="MultiplexingStream.Channel"/> carrying bulk data
    /// over a loopback socket, which is representative of the "large file transfer" scenario.
    /// </summary>
    [Config(typeof(BenchmarkConfig))]
    public class MultiplexingStreamBenchmark
    {
        private const int ChunkSize = 64 * 1024;

        private static readonly byte[] Chunk = new byte[ChunkSize];

        /// <summary>
        /// Gets or sets the major version of the multiplexing protocol to exercise.
        /// </summary>
        [Params(1, 2, 3)]
        public int ProtocolMajorVersion { get; set; }

        /// <summary>
        /// Gets or sets the number of bytes to transmit in each iteration.
        /// </summary>
        [Params(32 * 1024 * 1024)]
        public int TransferSize { get; set; }

        /// <summary>
        /// Transmits <see cref="TransferSize"/> bytes across one channel and waits for it all to be received.
        /// </summary>
        /// <returns>A task that tracks the transfer.</returns>
        [Benchmark]
        public async Task TransmitBulkDataOverOneChannel()
        {
            (Stream Client, Stream Server) transport = await CreateLoopbackStreamPairAsync();
            MultiplexingStream.Options options1 = new() { ProtocolMajorVersion = this.ProtocolMajorVersion };
            MultiplexingStream.Options options2 = new() { ProtocolMajorVersion = this.ProtocolMajorVersion };
            Task<MultiplexingStream> mx1Task = MultiplexingStream.CreateAsync(transport.Client, options1);
            Task<MultiplexingStream> mx2Task = MultiplexingStream.CreateAsync(transport.Server, options2);
            MultiplexingStream mx1 = await mx1Task;
            MultiplexingStream mx2 = await mx2Task;
            try
            {
                Task<MultiplexingStream.Channel> offer = mx1.OfferChannelAsync("bench");
                Task<MultiplexingStream.Channel> accept = mx2.AcceptChannelAsync("bench");
                MultiplexingStream.Channel sender = await offer;
                MultiplexingStream.Channel receiver = await accept;

                long transferSize = this.TransferSize;
                Task receiveTask = Task.Run(async delegate
                {
                    long bytesReceived = 0;
                    while (bytesReceived < transferSize)
                    {
                        ReadResult readResult = await receiver.Input.ReadAsync();
                        if (readResult.Buffer.IsEmpty && readResult.IsCompleted)
                        {
                            break;
                        }

                        bytesReceived += readResult.Buffer.Length;
                        receiver.Input.AdvanceTo(readResult.Buffer.End);
                    }
                });

                for (long bytesSent = 0; bytesSent < transferSize;)
                {
                    int bytesThisRound = (int)Math.Min(ChunkSize, transferSize - bytesSent);
                    Chunk.AsSpan(0, bytesThisRound).CopyTo(sender.Output.GetSpan(bytesThisRound));
                    sender.Output.Advance(bytesThisRound);
                    await sender.Output.FlushAsync();
                    bytesSent += bytesThisRound;
                }

                await receiveTask;
            }
            finally
            {
                await mx1.DisposeAsync();
                await mx2.DisposeAsync();
            }
        }

        private static async Task<(Stream Client, Stream Server)> CreateLoopbackStreamPairAsync()
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
                TcpClient client = new();
                await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                TcpClient server = await acceptTask;
                client.NoDelay = true;
                server.NoDelay = true;
                return (client.GetStream(), server.GetStream());
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
