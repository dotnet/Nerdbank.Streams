// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
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
        this.serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        this.clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
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
        return TplExtensions.CompletedTask;
    }

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
            byte[][] clientBuffers = Enumerable.Range(1, ChannelCount).Select(i => new byte[SegmentSize]).ToArray();
            byte[][] serverBuffers = Enumerable.Range(1, ChannelCount).Select(i => new byte[SegmentSize]).ToArray();

            var (mxServer, mxClient) = await Task.WhenAll(
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
                                 var channel = await mxServer.CreateChannelAsync(string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);
                                 for (int i = 0; i < segmentCount / ChannelCount; i++)
                                 {
                                     await channel.WriteAsync(serverBuffer, 0, serverBuffer.Length, this.TimeoutToken);
                                 }

                                 await channel.FlushAsync();
                             })));
                    }),
                    Task.Run(async delegate
                    {
                        await Task.WhenAll(
                            Enumerable.Range(1, ChannelCount).Select(c => Task.Run(async delegate
                            {
                                byte[] clientBuffer = clientBuffers[c - 1];
                                var channel = await mxClient.AcceptChannelAsync(string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);
                                int expectedTotalBytesRead = segmentCount / ChannelCount * SegmentSize;
                                int totalBytesRead = 0;
                                int bytesJustRead;
                                do
                                {
                                    bytesJustRead = await channel.ReadAsync(clientBuffer, 0, clientBuffer.Length, this.TimeoutToken);
                                    totalBytesRead += bytesJustRead;
                                }
                                while (totalBytesRead < expectedTotalBytesRead);
                                Assert.Equal(expectedTotalBytesRead, totalBytesRead);
                            })));
                    })).WithCancellation(this.TimeoutToken);
            }
        }
    }
}
