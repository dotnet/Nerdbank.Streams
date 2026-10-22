// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using NSubstitute;
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

    [Theory]
    [PairwiseData]
    public async Task ReaderCompletionRacingChannelDisposalDoesNotCloseStream(bool completeAsync)
    {
        await this.mx1.DisposeAsync();
        await this.mx2.DisposeAsync();
        (this.transport1, this.transport2) = FullDuplexStream.CreatePair();
        var monitoredTransport = new MonitoringStream(this.transport2);
        var options = new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion };
        Task<MultiplexingStream> mx1Task = MultiplexingStream.CreateAsync(this.transport1, options, this.TimeoutToken);
        Task<MultiplexingStream> mx2Task = MultiplexingStream.CreateAsync(monitoredTransport, options, this.TimeoutToken);
        this.mx1 = await mx1Task;
        this.mx2 = await mx2Task;

        (MultiplexingStream.Channel control1, MultiplexingStream.Channel control2) = await this.EstablishChannelsAsync("control");
        var traceSource = new TraceSource("closing", SourceLevels.Information);
        TraceListener listener = Substitute.For<TraceListener>();
        traceSource.Listeners.Add(listener);
        Task<MultiplexingStream.Channel> offerTask = this.mx1.OfferChannelAsync(
            "closing", new MultiplexingStream.ChannelOptions { TraceSource = traceSource }, this.TimeoutToken);
        Task<MultiplexingStream.Channel> acceptTask = this.mx2.AcceptChannelAsync("closing", this.TimeoutToken);
        MultiplexingStream.Channel a = await offerTask;
        MultiplexingStream.Channel b = await acceptTask;

        // Terminate after the notification is prepared, but before it enters the send queue.
        listener.When(l => l.TraceEvent(
            Arg.Any<TraceEventCache?>(), Arg.Any<string>(), TraceEventType.Information, 0, "Local reader completed."))
            .Do(_ => a.Dispose());

        var readingPaused = new AsyncManualResetEvent();
        using var resumeReading = new ManualResetEventSlim();
        monitoredTransport.WillReadAny += PauseReading;
        byte[] payload = new byte[] { 1, 2, 3 };
        try
        {
            // Keep the peer from acknowledging termination before the late notification is sent.
            await control1.Output.WriteAsync(payload, this.TimeoutToken);
            await readingPaused.WaitAsync(this.TimeoutToken);
            if (completeAsync)
            {
                await a.Input.CompleteAsync();
            }
            else
            {
                a.Input.Complete();
            }

            Assert.True(a.IsDisposed);
            Assert.False(this.mx1.Completion.IsFaulted, this.mx1.Completion.Exception?.ToString());
        }
        finally
        {
            monitoredTransport.WillReadAny -= PauseReading;
            resumeReading.Set();
        }

        await b.Completion.WithCancellation(this.TimeoutToken);
        await this.ReadAtLeastAsync(control2.Input, payload.Length);
        await this.TransmitAndVerifyAsync(control1.AsStream(), control2.AsStream(), payload);

        void PauseReading(object sender, ReadOnlySpan<byte> buffer)
        {
            monitoredTransport.WillReadAny -= PauseReading;
            readingPaused.Set();
            resumeReading.Wait(this.TimeoutToken);
        }
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
}
