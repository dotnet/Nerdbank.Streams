// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class MultiplexingStreamV2Tests : MultiplexingStreamTests
{
    public MultiplexingStreamV2Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override int ProtocolMajorVersion => 2;

    [Fact]
    public async Task Backpressure()
    {
        long backpressureThreshold = this.mx1.DefaultChannelReceivingWindowSize;
        var (a, b) = await this.EstablishChannelsAsync("a");

        var biteSizeChunk = new byte[backpressureThreshold * 2 / 5];
        var hugeChunk = new byte[backpressureThreshold * 2]; // enough to fill the remote and local windows
        a.Output.Write(hugeChunk);
        Task flushTask = a.Output.FlushAsync(this.TimeoutToken).AsTask();
        await Task.Delay(ExpectedTimeout);
        Assert.False(flushTask.IsCompleted);

        // Verify that another channel can be created and communicate while the first channel is still blocked.
        var (c, d) = await this.EstablishChannelsAsync("b");
        for (int i = 0; i < 5; i++)
        {
            c.Output.Write(biteSizeChunk);
            await c.Output.FlushAsync(this.TimeoutToken);
            await this.DrainAsync(d.Input, biteSizeChunk.Length);
        }

        // Assert that the original channel is still blocked.
        Assert.False(flushTask.IsCompleted);

        // Verify that the blocked channel still accepts communication going the other way.
        for (int i = 0; i < 5; i++)
        {
            b.Output.Write(biteSizeChunk);
            await b.Output.FlushAsync(this.TimeoutToken);
            await this.DrainAsync(a.Input, biteSizeChunk.Length);
        }

        // Assert that the original channel is still blocked.
        Assert.False(flushTask.IsCompleted);

        // Now read from the channel and verify it unblocks the writer.
        await this.DrainAsync(b.Input, hugeChunk.Length);

        await flushTask.WithCancellation(this.TimeoutToken);
        await CompleteChannelsAsync(a, b, c, d);
    }

    [Fact]
    public async Task Backpressure_FullButNeedMoreBytesToProcess()
    {
        var (a, b) = await this.EstablishChannelsAsync("a");

        // Write far more than would be allowed.
        long bytesWritten = this.mx2.DefaultChannelReceivingWindowSize * 5;
        this.Logger.WriteLine("Writing {0} bytes.", bytesWritten);
        Task<FlushResult> writeTask = a.Output.WriteAsync(new byte[bytesWritten], this.TimeoutToken).AsTask();

        while (true)
        {
            var readResult = await b.Input.ReadAsync(this.TimeoutToken);
            this.Logger.WriteLine("Read returned buffer with length: {0}", readResult.Buffer.Length);

            if (readResult.Buffer.Length < bytesWritten)
            {
                // Demand more by claiming to have examined everything.
                b.Input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            }
            else
            {
                // We got it all at once. So go ahead and consume it.
                b.Input.AdvanceTo(readResult.Buffer.End);
                break;
            }
        }

        await writeTask;
    }

    [Fact]
    public async Task Backpressure_ExistingPipe()
    {
        const int backpressureThreshold = 80 * 1024;
        var mx2Pipe = FullDuplexStream.CreatePipePair(new PipeOptions(pauseWriterThreshold: backpressureThreshold));
        var mx1ChannelTask = this.mx1.OfferChannelAsync("a", this.TimeoutToken);
        var mx2ChannelTask = this.mx2.AcceptChannelAsync(
            "a",
            new MultiplexingStream.ChannelOptions
            {
                ExistingPipe = mx2Pipe.Item1,
                ChannelReceivingWindowSize = backpressureThreshold,
            },
            this.TimeoutToken);
        var channels = await WhenAllSucceedOrAnyFail(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);
        var (a, b) = (channels[0], channels[1]);

        // Write far more than would be allowed.
        const int bytesWritten = backpressureThreshold * 5;
        this.Logger.WriteLine("Writing {0} bytes.", bytesWritten);
        Task<FlushResult> writeTask = a.Output.WriteAsync(new byte[bytesWritten], this.TimeoutToken).AsTask();

        while (true)
        {
            var readResult = await mx2Pipe.Item2.Input.ReadAsync(this.TimeoutToken);
            this.Logger.WriteLine("Read returned buffer with length: {0}", readResult.Buffer.Length);

            if (readResult.Buffer.Length < bytesWritten)
            {
                // Demand more by claiming to have examined everything.
                mx2Pipe.Item2.Input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            }
            else
            {
                // We got it all at once. So go ahead and consume it.
                mx2Pipe.Item2.Input.AdvanceTo(readResult.Buffer.End);
                break;
            }
        }

        await writeTask;
    }

    /// <summary>
    /// Regression test for <see href="https://github.com/AArnott/Nerdbank.Streams/issues/253">#253</see>.
    /// </summary>
    /// <devremarks>
    /// This test requires very careful timing with the debugger to actually hit the bug it was designed to identify. Specifically:
    /// * mx2 has to send the ChannelTerminated message before receiving it from mx1 (so that it puts the channel into its channelsPendingTermination collection).
    /// * mx2 channel must be disposed AFTER Channel.LocalContentExamined's IsDisposed check
    /// * mx2's ChannelTerminated frame must be sent BEFORE LocalContentExamined posts the ContentProcessed frame.
    /// </devremarks>
    [Fact]
    public async Task CompleteReadingAfterChannelTerminated()
    {
        long backpressureThreshold = this.mx1.DefaultChannelReceivingWindowSize;
        var (a, b) = await this.EstablishChannelsAsync("a");

        await a.Output.WriteAsync(new byte[30 * 1024], this.TimeoutToken);
        await a.Output.CompleteAsync();

        this.Logger.WriteLine("Calling ReadAsync");
        ReadResult readResult = await b.Input.ReadAsync(this.TimeoutToken);
        this.Logger.WriteLine("ReadAsync returned");

        await b.Output.CompleteAsync();

        this.Logger.WriteLine("Calling AdvanceTo");
        b.Input.AdvanceTo(readResult.Buffer.End);

        await b.Input.CompleteAsync();
        await a.Input.CompleteAsync();
    }
}
