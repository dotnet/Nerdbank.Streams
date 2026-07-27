// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;

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
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        byte[]? biteSizeChunk = new byte[backpressureThreshold * 2 / 5];
        byte[]? hugeChunk = new byte[backpressureThreshold * 2]; // enough to fill the remote and local windows
        a.Output.Write(hugeChunk);
        Task flushTask = a.Output.FlushAsync(this.TimeoutToken).AsTask();
        await Task.Delay(ExpectedTimeout);
        Assert.False(flushTask.IsCompleted);

        // Verify that another channel can be created and communicate while the first channel is still blocked.
        (MultiplexingStream.Channel c, MultiplexingStream.Channel d) = await this.EstablishChannelsAsync("b");
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
    public async Task ReaderCompletionUnblocksRemoteWriter()
    {
        long backpressureThreshold = this.mx1.DefaultChannelReceivingWindowSize;
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        // Write more than the remote window can accept so the flush cannot complete.
        a.Output.Write(new byte[backpressureThreshold * 2]);
        Task<FlushResult> flushTask = a.Output.FlushAsync(this.TimeoutToken).AsTask();
        await Task.Delay(ExpectedTimeout);
        Assert.False(flushTask.IsCompleted);

        // The remote party gives up on reading. This should release our blocked writer
        // rather than leave it waiting forever for window capacity that will never come.
        await b.Input.CompleteAsync();

        await flushTask.WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task Backpressure_FullButNeedMoreBytesToProcess()
    {
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        // Write far more than would be allowed.
        long bytesWritten = this.mx2.DefaultChannelReceivingWindowSize * 5;
        this.Logger.WriteLine("Writing {0} bytes.", bytesWritten);
        Task<FlushResult> writeTask = a.Output.WriteAsync(new byte[bytesWritten], this.TimeoutToken).AsTask();

        while (true)
        {
            ReadResult readResult = await b.Input.ReadAsync(this.TimeoutToken);
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
        (IDuplexPipe, IDuplexPipe) mx2Pipe = FullDuplexStream.CreatePipePair(new PipeOptions(pauseWriterThreshold: backpressureThreshold));
        Task<MultiplexingStream.Channel>? mx1ChannelTask = this.mx1.OfferChannelAsync("a", this.TimeoutToken);
        Task<MultiplexingStream.Channel>? mx2ChannelTask = this.mx2.AcceptChannelAsync(
            "a",
            new MultiplexingStream.ChannelOptions
            {
                ExistingPipe = mx2Pipe.Item1,
                ChannelReceivingWindowSize = backpressureThreshold,
            },
            this.TimeoutToken);
        MultiplexingStream.Channel[]? channels = await WhenAllSucceedOrAnyFail(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = (channels[0], channels[1]);

        // Write far more than would be allowed.
        const int bytesWritten = backpressureThreshold * 5;
        this.Logger.WriteLine("Writing {0} bytes.", bytesWritten);
        Task<FlushResult> writeTask = a.Output.WriteAsync(new byte[bytesWritten], this.TimeoutToken).AsTask();

        while (true)
        {
            ReadResult readResult = await mx2Pipe.Item2.Input.ReadAsync(this.TimeoutToken);
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

    [Fact]
    public async Task AcceptChannelAsync_SmallReceivingWindowSize()
    {
        const int offeredWindowSize = 16;
        const int acceptedWindowSize = 64;

        Task<MultiplexingStream.Channel> offeredChannelTask = this.mx1.OfferChannelAsync(
            "small-window",
            new MultiplexingStream.ChannelOptions { ChannelReceivingWindowSize = offeredWindowSize },
            this.TimeoutToken);
        Task<MultiplexingStream.Channel> acceptedChannelTask = this.mx2.AcceptChannelAsync(
            "small-window",
            new MultiplexingStream.ChannelOptions { ChannelReceivingWindowSize = acceptedWindowSize },
            this.TimeoutToken);

        MultiplexingStream.Channel[] channels = await WhenAllSucceedOrAnyFail(offeredChannelTask, acceptedChannelTask).WithCancellation(this.TimeoutToken);

        await this.TransmitAndVerifyAsync(channels[0].AsStream(), channels[1].AsStream(), new byte[] { 1, 2, 3 });
        await this.TransmitAndVerifyAsync(channels[1].AsStream(), channels[0].AsStream(), new byte[] { 4, 5, 6 });

        await CompleteChannelsAsync(channels);
    }

    [Fact]
    public async Task Backpressure_CopyToAsync()
    {
        long backpressureThreshold = this.mx1.DefaultChannelReceivingWindowSize;
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        byte[]? hugeChunk = new byte[backpressureThreshold * 2]; // enough to fill the remote and local windows
        a.Output.Write(hugeChunk);
        Task flushTask = Task.Run(async delegate
        {
            await a.Output.FlushAsync(this.TimeoutToken);
            await a.Output.CompleteAsync();
        });

        // Now read from the channel and verify it unblocks the writer, using CopyToAsync specifically.
        long drainedBytesCount = await this.DrainReaderTillCompletedAsync(b.Input, useCopyToAsync: true);
        Assert.Equal(hugeChunk.Length, drainedBytesCount);

        await flushTask.WithCancellation(this.TimeoutToken);
        await CompleteChannelsAsync(a, b);
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
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

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

    /// <summary>
    /// Verifies that a sender which fills the receiving window always earns credit back once the
    /// receiver drains it, across window sizes both far above and far below the maximum frame length.
    /// </summary>
    /// <param name="windowSize">The receiving window size to configure for the connection.</param>
    /// <returns>A task that tracks the test.</returns>
    /// <remarks>
    /// The receiver acknowledges data only after a threshold of it has been examined. If that threshold
    /// could ever exceed the window itself, a sender that filled the window would wait forever for credit
    /// the receiver would never send. The window sizes below deliberately straddle the maximum frame
    /// length, since the threshold is clamped at that length and therefore relates to the window
    /// differently on either side of it.
    /// </remarks>
    [Theory]
    [InlineData(1024)]
    [InlineData(20 * 1024)]
    [InlineData(100 * 1024)]
    [InlineData(1024 * 1024)]
    public async Task Backpressure_CreditIsReturnedForAnyWindowSize(int windowSize)
    {
        // The window must be set on the stream. A channel may only raise its window above the
        // stream default, never lower it, so ChannelOptions alone cannot produce a small window.
        await this.ReinitializeMxStreamsAsync(new MultiplexingStream.Options { DefaultChannelReceivingWindowSize = windowSize });
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        // Send several windows' worth so that the sender must block for credit repeatedly.
        int bytesToSend = windowSize * 4;
        Task writeTask = Task.Run(async delegate
        {
            await a.Output.WriteAsync(new byte[bytesToSend], this.TimeoutToken);
            await a.Output.CompleteAsync();
        });

        long bytesRead = 0;
        while (bytesRead < bytesToSend)
        {
            ReadResult readResult = await b.Input.ReadAsync(this.TimeoutToken);
            if (readResult.Buffer.IsEmpty && readResult.IsCompleted)
            {
                break;
            }

            bytesRead += readResult.Buffer.Length;
            b.Input.AdvanceTo(readResult.Buffer.End);
        }

        await writeTask.WithCancellation(this.TimeoutToken);
        Assert.Equal(bytesToSend, bytesRead);
    }

    /// <summary>
    /// Verifies that a receiver which examines data in increments far smaller than the window
    /// still allows the transfer to complete.
    /// </summary>
    /// <returns>A task that tracks the test.</returns>
    /// <remarks>
    /// This is the shape most likely to stall if the receiver waits for too much data to accumulate
    /// before acknowledging it: the sender fills the window and stops, while the reader consumes in
    /// pieces far too small to individually cross the acknowledgement threshold.
    /// </remarks>
    [Fact]
    public async Task Backpressure_SmallReadsStillMakeProgress()
    {
        const int windowSize = 1024 * 1024;
        const int readIncrement = 4 * 1024;
        await this.ReinitializeMxStreamsAsync(new MultiplexingStream.Options { DefaultChannelReceivingWindowSize = windowSize });
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        const int bytesToSend = windowSize * 3;
        Task writeTask = Task.Run(async delegate
        {
            await a.Output.WriteAsync(new byte[bytesToSend], this.TimeoutToken);
            await a.Output.CompleteAsync();
        });

        long bytesRead = 0;
        while (bytesRead < bytesToSend)
        {
            ReadResult readResult = await b.Input.ReadAsync(this.TimeoutToken);
            if (readResult.Buffer.IsEmpty && readResult.IsCompleted)
            {
                break;
            }

            // Consume only a small slice at a time, examining no more than we consume.
            long bytesThisRound = Math.Min(readIncrement, readResult.Buffer.Length);
            SequencePosition position = readResult.Buffer.GetPosition(bytesThisRound);
            b.Input.AdvanceTo(position, position);
            bytesRead += bytesThisRound;
        }

        await writeTask.WithCancellation(this.TimeoutToken);
        Assert.Equal(bytesToSend, bytesRead);
    }

    /// <summary>
    /// Verifies that a channel whose sender is repeatedly throttled by the receiving window
    /// ends up with a larger window than it started with.
    /// </summary>
    [Fact]
    public async Task WindowGrows_WhenSenderIsThrottled()
    {
        long initialWindow = this.mx1.DefaultChannelReceivingWindowSize;
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        await this.ThrottleSenderAsync(a, b, initialWindow, rounds: 2);

        // With no reader draining it, the amount that can be written before the flush blocks is bounded by
        // the receiving window. Twice the original window would not have fit before the channel grew.
        a.Output.Write(new byte[initialWindow * 2]);
        await a.Output.FlushAsync(this.TimeoutToken).AsTask().WithCancellation(this.TimeoutToken);

        await this.DrainAsync(b.Input, initialWindow * 2);
        await CompleteChannelsAsync(a, b);
    }

    /// <summary>
    /// Verifies that window growth stops at <see cref="MultiplexingStream.Options.MaxChannelReceivingWindowSize"/>.
    /// </summary>
    [Fact]
    public async Task WindowGrowth_IsCappedPerChannel()
    {
        await this.ReinitializeMxStreamsAsync(new MultiplexingStream.Options
        {
            MaxChannelReceivingWindowSize = 2 * new MultiplexingStream.Options().DefaultChannelReceivingWindowSize,
        });

        long initialWindow = this.mx1.DefaultChannelReceivingWindowSize;
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        await this.ThrottleSenderAsync(a, b, initialWindow, rounds: 2);

        // The window may have doubled once, but no further, so this must not fit.
        a.Output.Write(new byte[initialWindow * 4]);
        Task flushTask = a.Output.FlushAsync(this.TimeoutToken).AsTask();
        await Task.Delay(ExpectedTimeout);
        Assert.False(flushTask.IsCompleted);

        await this.DrainAsync(b.Input, initialWindow * 4);
        await flushTask.WithCancellation(this.TimeoutToken);
        await CompleteChannelsAsync(a, b);
    }

    /// <summary>
    /// Verifies that the stream-wide growth budget can veto growth that the channel would otherwise take,
    /// which is what bounds the memory a stream with many busy channels may commit.
    /// </summary>
    [Fact]
    public async Task WindowDoesNotGrow_WhenStreamBudgetIsExhausted()
    {
        await this.ReinitializeMxStreamsAsync(new MultiplexingStream.Options
        {
            MaxTotalChannelReceivingWindowSize = 0,
        });

        long initialWindow = this.mx1.DefaultChannelReceivingWindowSize;
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        await this.ThrottleSenderAsync(a, b, initialWindow, rounds: 2);

        // Without budget, the channel must still behave exactly as it does on v3.
        a.Output.Write(new byte[initialWindow * 2]);
        Task flushTask = a.Output.FlushAsync(this.TimeoutToken).AsTask();
        await Task.Delay(ExpectedTimeout);
        Assert.False(flushTask.IsCompleted);

        await this.DrainAsync(b.Input, initialWindow * 2);
        await flushTask.WithCancellation(this.TimeoutToken);
        await CompleteChannelsAsync(a, b);
    }

    /// <summary>
    /// Verifies that a channel that never fills its window is left at its original size,
    /// so that idle or low-rate channels do not consume the stream's growth budget.
    /// </summary>
    [Fact]
    public async Task WindowDoesNotGrow_ForUnsaturatedChannel()
    {
        long initialWindow = this.mx1.DefaultChannelReceivingWindowSize;
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        // Transfer well over a window's worth of data in small pieces, draining after each one
        // so the window is never anywhere near full.
        byte[] smallChunk = new byte[initialWindow / 16];
        for (int i = 0; i < 32; i++)
        {
            a.Output.Write(smallChunk);
            await a.Output.FlushAsync(this.TimeoutToken);
            await this.DrainAsync(b.Input, smallChunk.Length);

            // Allow the acknowledgment to reach the sender before another half-window accumulates.
            if ((i + 1) % 8 == 0)
            {
                await Task.Delay(ExpectedTimeout);
            }
        }

        // The window should not have grown, so twice the original window must not fit.
        a.Output.Write(new byte[initialWindow * 2]);
        Task flushTask = a.Output.FlushAsync(this.TimeoutToken).AsTask();
        await Task.Delay(ExpectedTimeout);
        Assert.False(flushTask.IsCompleted);

        await this.DrainAsync(b.Input, initialWindow * 2);
        await flushTask.WithCancellation(this.TimeoutToken);
        await CompleteChannelsAsync(a, b);
    }

    /// <summary>
    /// Repeatedly writes more than the receiving window and only then drains it, which guarantees the
    /// receiver observes a sender that is blocked on flow control.
    /// </summary>
    /// <param name="sender">The channel to write to.</param>
    /// <param name="receiver">The channel to read from.</param>
    /// <param name="window">The receiving window size that the channel started with.</param>
    /// <param name="rounds">The number of times to saturate and drain the window.</param>
    /// <returns>A task that completes when the transfers are done.</returns>
    private async Task ThrottleSenderAsync(MultiplexingStream.Channel sender, MultiplexingStream.Channel receiver, long window, int rounds)
    {
        for (int i = 0; i < rounds; i++)
        {
            // The window grows by a factor of 4 on each successful round, so scale what we write to match,
            // ensuring the sender is still throttled no matter how much it grew last time.
            long writeSize = (window * 2) << (2 * i);
            sender.Output.Write(new byte[writeSize]);
            Task flushTask = sender.Output.FlushAsync(this.TimeoutToken).AsTask();

            // Let the sender run out of credit before anyone reads, so that it reliably asks for a larger window.
            await Task.Delay(ExpectedTimeout);
            Assert.False(flushTask.IsCompleted);

            await this.DrainAsync(receiver.Input, writeSize);
            await flushTask.WithCancellation(this.TimeoutToken);

            // A growth request is only judged when the receiving side's reader runs out of work, since that is
            // the moment it becomes clear that the window rather than the consumer is the constraint.
            // Issue a read that cannot be satisfied so that moment arrives deterministically.
            Task<ReadResult> starvedRead = receiver.Input.ReadAsync(this.TimeoutToken).AsTask();
            await Task.Delay(ExpectedTimeout);
            receiver.Input.CancelPendingRead();
            ReadResult readResult = await starvedRead.WithCancellation(this.TimeoutToken);
            receiver.Input.AdvanceTo(readResult.Buffer.Start);
        }
    }
}
