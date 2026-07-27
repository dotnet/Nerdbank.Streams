// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;

    /// <summary>
    /// Common setup for benchmarks that measure a <see cref="MultiplexingStream"/> pair
    /// connected to each other over a loopback socket.
    /// </summary>
    /// <remarks>
    /// A loopback socket is fast enough that the multiplexing layer, rather than the transport,
    /// is the bottleneck. That is what makes these benchmarks sensitive to changes in framing
    /// and flow control.
    /// </remarks>
    [Config(typeof(MultiplexingStreamBenchmarkConfig))]
    public abstract class MultiplexingStreamBenchmarkBase
    {
        private TcpClient? client;
        private TcpClient? server;

        /// <summary>
        /// Gets or sets the major version of the multiplexing protocol to exercise.
        /// </summary>
        /// <remarks>
        /// Version 1 has no backpressure at all, so it serves as the ceiling against which
        /// the cost of flow control in later versions can be measured.
        /// Version 4 keeps v3's flow control but lets a throttled channel enlarge its window.
        /// </remarks>
        [Params(1, 2, 3, 4)]
        public int ProtocolMajorVersion { get; set; }

        /// <summary>
        /// Gets the multiplexing stream that offers channels.
        /// </summary>
        protected MultiplexingStream Mx1 { get; private set; } = null!;

        /// <summary>
        /// Gets the multiplexing stream that accepts channels.
        /// </summary>
        protected MultiplexingStream Mx2 { get; private set; } = null!;

        /// <summary>
        /// Establishes the loopback connection and the multiplexing streams.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup() => this.GlobalSetupAsync().GetAwaiter().GetResult();

        /// <summary>
        /// Tears down the multiplexing streams and the loopback connection.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup() => this.GlobalCleanupAsync().GetAwaiter().GetResult();

        /// <summary>
        /// Creates the options used for each end of the connection.
        /// </summary>
        /// <returns>The options.</returns>
        protected virtual MultiplexingStream.Options CreateOptions()
            => new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion };

        /// <summary>
        /// Invoked once after the connection is established, for derived classes that need to prepare
        /// channels or background loops that outlive a single operation.
        /// </summary>
        /// <returns>A task that tracks the work.</returns>
        /// <remarks>
        /// Derived classes use this rather than declaring their own <see cref="GlobalSetupAttribute"/>,
        /// which would leave it ambiguous whether the inherited one also runs.
        /// </remarks>
        protected virtual Task OnConnectedAsync() => Task.CompletedTask;

        /// <summary>
        /// Wraps each end of the transport before the multiplexing streams are built on top of it.
        /// </summary>
        /// <param name="transport">One end of the loopback connection.</param>
        /// <returns>The stream the multiplexing stream should use.</returns>
        /// <remarks>
        /// The default implementation returns <paramref name="transport"/> unchanged. Derived classes
        /// override this to interpose behavior such as artificial latency.
        /// </remarks>
        protected virtual Stream WrapTransport(Stream transport) => transport;

        /// <summary>
        /// Opens a channel with a unique name on both ends of the connection.
        /// </summary>
        /// <param name="name">A name that has not been used on this connection before.</param>
        /// <returns>The offered and accepted ends of the channel.</returns>
        protected async Task<(MultiplexingStream.Channel Sender, MultiplexingStream.Channel Receiver)> CreateChannelAsync(string name)
        {
            Task<MultiplexingStream.Channel> offerTask = this.Mx1.OfferChannelAsync(name);
            Task<MultiplexingStream.Channel> acceptTask = this.Mx2.AcceptChannelAsync(name);
            return (await offerTask, await acceptTask);
        }

        private async Task GlobalSetupAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
                this.client = new TcpClient();
                await this.client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                this.server = await acceptTask;
            }
            finally
            {
                listener.Stop();
            }

            // Disable Nagle so that small frames are not delayed, which would otherwise
            // dominate the latency-sensitive benchmarks.
            this.client.NoDelay = true;
            this.server.NoDelay = true;

            Stream clientStream = this.WrapTransport(this.client.GetStream());
            Stream serverStream = this.WrapTransport(this.server.GetStream());

            Task<MultiplexingStream> mx1Task = MultiplexingStream.CreateAsync(clientStream, this.CreateOptions());
            Task<MultiplexingStream> mx2Task = MultiplexingStream.CreateAsync(serverStream, this.CreateOptions());
            this.Mx1 = await mx1Task;
            this.Mx2 = await mx2Task;

            await this.OnConnectedAsync();
        }

        private async Task GlobalCleanupAsync()
        {
            if (this.Mx1 is not null)
            {
                await this.Mx1.DisposeAsync();
            }

            if (this.Mx2 is not null)
            {
                await this.Mx2.DisposeAsync();
            }

            this.client?.Dispose();
            this.server?.Dispose();
        }
    }
}
