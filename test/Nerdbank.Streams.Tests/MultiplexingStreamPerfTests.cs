// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;

public class MultiplexingStreamPerfTests : TestBase, IAsyncLifetime
{
    private const int SegmentSize = 5 * 1024;
    private const int SegmentCount = 100;
    private const int ChannelCount = 1;
    private readonly NamedPipeServerStream serverPipe;
    private readonly NamedPipeClientStream clientPipe;

    public MultiplexingStreamPerfTests(ITestOutputHelper logger)
        : base(logger)
    {
        string pipeName = Guid.NewGuid().ToString();
        this.serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        this.clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
    }

    public async Task InitializeAsync()
    {
        Task connectTask = this.serverPipe.WaitForConnectionAsync(this.TimeoutToken);
        await this.clientPipe.ConnectAsync(this.TimeoutToken);
        await connectTask;
    }

    public Task DisposeAsync()
    {
        this.serverPipe.Dispose();
        this.clientPipe.Dispose();
        return Task.CompletedTask;
    }

    [SkippableFact]
    public Task JsonRpcPerf_Pipe() => this.JsonRpcPerf(useChannel: false);

    [SkippableFact]
    public Task JsonRpcPerf_Channel() => this.JsonRpcPerf(useChannel: true);

    [SkippableFact]
    public async Task SendLargePayloadOnOneStream()
    {
        if (await this.ExecuteInIsolationAsync())
        {
            byte[] serverBuffer = new byte[SegmentSize];
            byte[] clientBuffer = new byte[SegmentSize];

            await this.WaitForQuietPeriodAsync();

            // Warm up
            await RunAsync(2);

            long memory1 = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            await RunAsync(SegmentCount);
            sw.Stop();
            long memory2 = GC.GetTotalMemory(false);
            long allocated = memory2 - memory1;
            this.Logger.WriteLine("{0} bytes allocated ({1} per segment)", allocated, allocated / SegmentCount);
            this.Logger.WriteLine("{0} bytes transmitted in each of {1} segments in {2}ms on 1 channel", SegmentSize, SegmentCount, sw.ElapsedMilliseconds);

            async Task RunAsync(int segmentCount)
            {
                await Task.WhenAll(
                    Task.Run(async delegate
                    {
                        for (int i = 0; i < segmentCount; i++)
                        {
                            await this.serverPipe.WriteAsync(serverBuffer, 0, serverBuffer.Length, this.TimeoutToken);
                            await this.serverPipe.FlushAsync();
                        }

                        await this.serverPipe.FlushAsync();
                    }),
                    Task.Run(async delegate
                    {
                        int totalBytesRead = 0;
                        int bytesJustRead;
                        do
                        {
                            bytesJustRead = await this.clientPipe.ReadAsync(clientBuffer, 0, clientBuffer.Length, this.TimeoutToken);
                            totalBytesRead += bytesJustRead;
                        }
                        while (totalBytesRead < segmentCount * SegmentSize);
                        Assert.Equal(segmentCount * SegmentSize, totalBytesRead);
                    })).WithCancellation(this.TimeoutToken);
            }
        }
    }

    [SkippableFact]
    public async Task SendLargePayloadOnManyChannels()
    {
        if (await this.ExecuteInIsolationAsync())
        {
            byte[][] serverBuffers = Enumerable.Range(1, ChannelCount).Select(i => new byte[SegmentSize]).ToArray();

            (MultiplexingStream mxServer, MultiplexingStream mxClient) = await Task.WhenAll(
                MultiplexingStream.CreateAsync(this.serverPipe, this.TimeoutToken).WithCancellation(this.TimeoutToken),
                MultiplexingStream.CreateAsync(this.clientPipe, this.TimeoutToken).WithCancellation(this.TimeoutToken));

            await this.WaitForQuietPeriodAsync();

            // Warm up
            await RunAsync(ChannelCount * 2);

            long memory1 = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            await RunAsync(SegmentCount);
            sw.Stop();
            long memory2 = GC.GetTotalMemory(false);
            long allocated = memory2 - memory1;
            this.Logger.WriteLine("{0} bytes allocated ({1} per segment)", allocated, allocated / SegmentCount);
            this.Logger.WriteLine("{0} bytes transmitted in each of {1} segments in {2}ms on {3} channel(s)", SegmentSize, SegmentCount, sw.ElapsedMilliseconds, ChannelCount);

            async Task RunAsync(int segmentCount)
            {
                Requires.Argument(segmentCount >= ChannelCount, nameof(segmentCount), "Cannot send {0} segments over {1} channels.", segmentCount, ChannelCount);
                await Task.WhenAll(
                    Task.Run(async delegate
                    {
                        await Task.WhenAll(
                            Enumerable.Range(1, ChannelCount).Select(c => Task.Run(async delegate
                            {
                                byte[] serverBuffer = serverBuffers[c - 1];
                                MultiplexingStream.Channel? channel = await mxServer.OfferChannelAsync(string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);
                                for (int i = 0; i < segmentCount / ChannelCount; i++)
                                {
                                    await channel.Output.WriteAsync(serverBuffer, this.TimeoutToken);
                                }
                            })));
                    }),
                    Task.Run(async delegate
                    {
                        await Task.WhenAll(
                            Enumerable.Range(1, ChannelCount).Select(c => Task.Run(async delegate
                            {
                                MultiplexingStream.Channel? channel = await mxClient.AcceptChannelAsync(string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);
                                int expectedTotalBytesRead = segmentCount / ChannelCount * SegmentSize;
                                int totalBytesRead = 0;
                                do
                                {
                                    System.IO.Pipelines.ReadResult readResult = await channel.Input.ReadAsync(this.TimeoutToken);
                                    totalBytesRead += (int)readResult.Buffer.Length;
                                    channel.Input.AdvanceTo(readResult.Buffer.End);
                                    readResult.ScrubAfterAdvanceTo();
                                }
                                while (totalBytesRead < expectedTotalBytesRead);
                                Assert.Equal(expectedTotalBytesRead, totalBytesRead);
                            })));
                    })).WithCancellation(this.TimeoutToken);
            }
        }
    }

    [Fact]
    public async Task TransmissionSpeedBaseline_Stream()
    {
        const long TotalSize = 1L * 1024 * 1024 * 1024;
        (Stream read, Stream write) = FullDuplexStream.CreatePair();

        Stopwatch sw = Stopwatch.StartNew();
        Task<long> readTask = ReadToBitBucketAsync(read, CancellationToken.None);
        Task<long> writeTask = WriteAsync(write, CancellationToken.None);
        await WhenAllSucceedOrAnyFail(writeTask, readTask);
        sw.Stop();
        long bytesRead = await readTask;
        long bytesWritten = await writeTask;
        this.Logger.WriteLine($"Wrote {bytesWritten / 1024 / 1024} MB in {sw.Elapsed}. Rate: {bytesWritten / (1024 * 1024) / sw.Elapsed.TotalSeconds:0} MBps.");
        this.Logger.WriteLine($"Read {bytesRead}.");

        static async Task<long> WriteAsync(Stream s, CancellationToken cancellationToken)
        {
            await Task.Yield().ConfigureAwait(false);
            var buffer = new byte[4096];
            long bytesToWrite = TotalSize;
            try
            {
                while (bytesToWrite > 0)
                {
                    int bytesToWriteThisTime = (int)Math.Min(bytesToWrite, buffer.Length);
                    await s.WriteAsync(buffer, 0, bytesToWriteThisTime, cancellationToken);
                    await s.FlushAsync(cancellationToken);
                    bytesToWrite -= bytesToWriteThisTime;
                }

                return TotalSize;
            }
            finally
            {
                s.Dispose();
            }
        }

        static async Task<long> ReadToBitBucketAsync(Stream s, CancellationToken cancellationToken)
        {
            await Task.Yield().ConfigureAwait(false);
            var buffer = new byte[4096];
            long totalBytesRead = 0, bytesRead;
            while ((bytesRead = await s.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                totalBytesRead += bytesRead;
            }

            return totalBytesRead;
        }
    }

    [Fact]
    public async Task TransmissionSpeedBaseline_Pipe()
    {
        const long TotalSize = 1L * 1024 * 1024 * 1024;
        (IDuplexPipe read, IDuplexPipe write) = FullDuplexStream.CreatePipePair();

        Stopwatch sw = Stopwatch.StartNew();
        Task<long> readTask = ReadToBitBucketAsync(read.Input, this.TimeoutToken);
        Task<long> writeTask = WriteAsync(write.Output, this.TimeoutToken);
        await WhenAllSucceedOrAnyFail(writeTask, readTask);
        sw.Stop();
        long bytesRead = await readTask;
        long bytesWritten = await writeTask;
        this.Logger.WriteLine($"Wrote {bytesWritten / 1024 / 1024} MB in {sw.Elapsed}. Rate: {bytesWritten / (1024 * 1024) / sw.Elapsed.TotalSeconds:0} MBps.");
        this.Logger.WriteLine($"Read {bytesRead}.");

        static async Task<long> WriteAsync(PipeWriter s, CancellationToken cancellationToken)
        {
            await Task.Yield().ConfigureAwait(false);
            var buffer = new byte[4096];
            long bytesToWrite = TotalSize;
            try
            {
                while (bytesToWrite > 0)
                {
                    int bytesToWriteThisTime = (int)Math.Min(bytesToWrite, buffer.Length);
                    await s.WriteAsync(buffer.AsMemory(0, bytesToWriteThisTime), cancellationToken);
                    bytesToWrite -= bytesToWriteThisTime;
                }

                await s.CompleteAsync();
                return TotalSize;
            }
            catch (Exception ex)
            {
                await s.CompleteAsync(ex);
                throw;
            }
        }

        static async Task<long> ReadToBitBucketAsync(PipeReader s, CancellationToken cancellationToken)
        {
            await Task.Yield().ConfigureAwait(false);
            long totalBytesRead = 0;
            while (true)
            {
                ReadResult read = await s.ReadAsync(cancellationToken);
                totalBytesRead += read.Buffer.Length;
                s.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    break;
                }
            }

            return totalBytesRead;
        }
    }

    [Theory, PairwiseData]
    public async Task TransmissionSpeed_Channel([CombinatorialRange(1, 3)] int protocolVersion)
    {
        const long TotalSize = 1L * 1024 * 1024 * 1024;
        MultiplexingStream.Options options = new()
        {
            ProtocolMajorVersion = protocolVersion,
        };
        (MultiplexingStream.Channel ch1, MultiplexingStream.Channel ch2) = await this.EstablishChannelsAsync("perftest", options);

        Stopwatch sw = Stopwatch.StartNew();
        Task<long> readTask = ReadToBitBucketAsync(ch1.Input, this.TimeoutToken);
        Task<long> writeTask = WriteAsync(ch2.Output, this.TimeoutToken);
        await WhenAllSucceedOrAnyFail(writeTask, readTask);
        sw.Stop();
        long bytesRead = await readTask;
        long bytesWritten = await writeTask;
        this.Logger.WriteLine($"Wrote {bytesWritten / 1024 / 1024} MB in {sw.Elapsed}. Rate: {bytesWritten / (1024 * 1024) / sw.Elapsed.TotalSeconds:0} MBps.");
        this.Logger.WriteLine($"Read {bytesRead}.");

        static async Task<long> WriteAsync(PipeWriter s, CancellationToken cancellationToken)
        {
            await Task.Yield().ConfigureAwait(false);
            var buffer = new byte[4096];
            long bytesToWrite = TotalSize;
            try
            {
                while (bytesToWrite > 0)
                {
                    int bytesToWriteThisTime = (int)Math.Min(bytesToWrite, buffer.Length);
                    await s.WriteAsync(buffer.AsMemory(0, bytesToWriteThisTime), cancellationToken);
                    bytesToWrite -= bytesToWriteThisTime;
                }

                await s.CompleteAsync();
                return TotalSize;
            }
            catch (Exception ex)
            {
                await s.CompleteAsync(ex);
                throw;
            }
        }

        static async Task<long> ReadToBitBucketAsync(PipeReader s, CancellationToken cancellationToken)
        {
            await Task.Yield().ConfigureAwait(false);
            long totalBytesRead = 0;
            while (true)
            {
                ReadResult read = await s.ReadAsync(cancellationToken);
                totalBytesRead += read.Buffer.Length;
                s.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    break;
                }
            }

            return totalBytesRead;
        }
    }

    protected async Task<(MultiplexingStream.Channel Party1, MultiplexingStream.Channel Party2)> EstablishChannelsAsync(string identifier, MultiplexingStream.Options? options = null, long? receivingWindowSize = null)
    {
        (Stream pipe1, Stream pipe2) = FullDuplexStream.CreatePair();

        (MultiplexingStream mxServer, MultiplexingStream mxClient) = await Task.WhenAll(
            MultiplexingStream.CreateAsync(pipe1, options, this.TimeoutToken).WithCancellation(this.TimeoutToken),
            MultiplexingStream.CreateAsync(pipe2, options, this.TimeoutToken).WithCancellation(this.TimeoutToken));

        var channelOptions = new MultiplexingStream.ChannelOptions { ChannelReceivingWindowSize = receivingWindowSize };
        Task<MultiplexingStream.Channel>? mx1ChannelTask = mxClient.OfferChannelAsync(identifier, channelOptions, this.TimeoutToken);
        Task<MultiplexingStream.Channel>? mx2ChannelTask = mxServer.AcceptChannelAsync(identifier, channelOptions, this.TimeoutToken);
        MultiplexingStream.Channel[]? channels = await WhenAllSucceedOrAnyFail(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);
        Assert.NotNull(channels[0]);
        Assert.NotNull(channels[1]);
        return (channels[0], channels[1]);
    }

    private async Task JsonRpcPerf(bool useChannel, [CallerMemberName] string? testMethodName = null)
    {
        if (await this.ExecuteInIsolationAsync(testMethodName))
        {
            Stream serverStream;
            Stream clientStream;
            if (useChannel)
            {
                (MultiplexingStream mxServer, MultiplexingStream mxClient) = await Task.WhenAll(
                    MultiplexingStream.CreateAsync(this.serverPipe, this.TimeoutToken).WithCancellation(this.TimeoutToken),
                    MultiplexingStream.CreateAsync(this.clientPipe, this.TimeoutToken).WithCancellation(this.TimeoutToken));

                (MultiplexingStream.Channel serverChannel, MultiplexingStream.Channel clientChannel) = await Task.WhenAll(
                    mxServer.AcceptChannelAsync(string.Empty, this.TimeoutToken),
                    mxClient.OfferChannelAsync(string.Empty, this.TimeoutToken));

                clientStream = clientChannel.AsStream();
                serverStream = serverChannel.AsStream();
            }
            else
            {
                clientStream = this.clientPipe;
                serverStream = this.serverPipe;
            }

            var clientRpc = JsonRpc.Attach(clientStream);
            var serverRpc = JsonRpc.Attach(serverStream, new RpcServer());

            await this.WaitForQuietPeriodAsync();

            // Warm up
            await RunAsync(1);

            const int iterations = 1000;
            int[] gcCountBefore = new int[GC.MaxGeneration + 1];
            int[] gcCountAfter = new int[GC.MaxGeneration + 1];
            long memory1 = GC.GetTotalMemory(true);

            bool noGCStarted = GC.TryStartNoGCRegion(32 * 1024 * 1024);

            for (int i = 0; i <= GC.MaxGeneration; i++)
            {
                gcCountBefore[i] = GC.CollectionCount(i);
            }

            var sw = Stopwatch.StartNew();
            await RunAsync(iterations);
            sw.Stop();

            for (int i = 0; i < gcCountAfter.Length; i++)
            {
                gcCountAfter[i] = GC.CollectionCount(i);
            }

            long memory2 = GC.GetTotalMemory(false);
            if (noGCStarted)
            {
                try
                {
                    GC.EndNoGCRegion();
                }
                catch (InvalidOperationException ex)
                {
                    this.Logger.WriteLine("WARNING: GC suppression failed with: {0}", ex.Message);
                }
            }

            long allocated = memory2 - memory1;
            this.Logger.WriteLine("{0} bytes allocated ({1} per iteration)", allocated, allocated / iterations);
            this.Logger.WriteLine("Elapsed time: {0}ms ({1}ms per iteration)", sw.ElapsedMilliseconds, (double)sw.ElapsedMilliseconds / iterations);

            for (int i = 0; i <= GC.MaxGeneration; i++)
            {
                if (gcCountAfter[i] > gcCountBefore[i])
                {
                    this.Logger.WriteLine("WARNING: Gen {0} GC occurred {1} times during testing. Results are probably totally wrong.", i, gcCountAfter[i] - gcCountBefore[i]);
                }
            }

            async Task RunAsync(int repetitions)
            {
                for (int i = 0; i < repetitions; i++)
                {
                    int sum = await clientRpc.InvokeAsync<int>(nameof(RpcServer.Add), 1, 2);
                }
            }
        }
    }

    private class RpcServer
    {
        public int Add(int a, int b) => a + b;
    }
}
