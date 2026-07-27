// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;

public class MultiplexingStreamV4Tests : MultiplexingStreamV3Tests
{
    public MultiplexingStreamV4Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override int ProtocolMajorVersion => 4;

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
