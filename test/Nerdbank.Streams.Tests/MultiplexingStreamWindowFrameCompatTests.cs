// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;

/// <summary>
/// Verifies that the window tuning frames are purely additive to the protocol, by simulating a peer
/// that predates them and therefore silently ignores them.
/// </summary>
/// <remarks>
/// This is the premise the whole feature rests on: because both the .NET and JS implementations dispatch
/// frames through a <c>default:</c> case that ignores unrecognized control codes, and because frames are
/// self-delimiting, dropping these frames costs the throughput win but cannot corrupt or hang a connection.
/// </remarks>
public class MultiplexingStreamWindowFrameCompatTests : TestBase
{
    private int droppedFrames;

    public MultiplexingStreamWindowFrameCompatTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Verifies that a connection whose window tuning frames are all discarded in flight still transfers
    /// every byte correctly and shuts down cleanly, exactly as if neither party had ever implemented them.
    /// </summary>
    [Fact]
    public async Task TransferSucceeds_WhenWindowFramesAreDropped()
    {
        (Stream Item1, Stream Item2) left = FullDuplexStream.CreatePair();
        (Stream Item1, Stream Item2) right = FullDuplexStream.CreatePair();

        // Relay the two halves to each other, discarding the frames an older peer would not recognize.
        Task relay1 = this.RelayAsync(left.Item2, right.Item2, this.TimeoutToken);
        Task relay2 = this.RelayAsync(right.Item2, left.Item2, this.TimeoutToken);

        var options = new MultiplexingStream.Options { ProtocolMajorVersion = 3 };
        MultiplexingStream mx1 = MultiplexingStream.Create(left.Item1, options);
        MultiplexingStream mx2 = MultiplexingStream.Create(right.Item1, options);

        Task<MultiplexingStream.Channel> offerTask = mx1.OfferChannelAsync("c", this.TimeoutToken);
        Task<MultiplexingStream.Channel> acceptTask = mx2.AcceptChannelAsync("c", this.TimeoutToken);
        MultiplexingStream.Channel sender = await offerTask.WithCancellation(this.TimeoutToken);
        MultiplexingStream.Channel receiver = await acceptTask.WithCancellation(this.TimeoutToken);

        // Send several times the default window so the sender is forced to stall repeatedly,
        // which is what provokes the growth requests that this test drops.
        const int TransferSize = 4 * 1024 * 1024;
        byte[] payload = this.GetRandomBuffer(TransferSize);
        Task sendTask = Task.Run(
            async () =>
            {
                await sender.Output.WriteAsync(payload, this.TimeoutToken);
                await sender.Output.CompleteAsync();
            },
            this.TimeoutToken);

        // Let the sender run ahead with nobody reading, so it is certain to exhaust its window and ask for
        // a larger one. A prompt reader on a zero-latency pipe may never let the sender stall at all.
        await Task.Delay(ExpectedTimeout, this.TimeoutToken);

        byte[] received = new byte[TransferSize];
        int bytesRead = 0;
        while (bytesRead < TransferSize)
        {
            int justRead = await receiver.AsStream().ReadAsync(received, bytesRead, TransferSize - bytesRead, this.TimeoutToken);
            Assert.NotEqual(0, justRead);
            bytesRead += justRead;
        }

        await sendTask.WithCancellation(this.TimeoutToken);
        Assert.Equal<byte>(payload, received);

        // Guard against a vacuous pass: the transfer is far larger than the default window,
        // so the sender must have stalled and asked for a larger one at least once.
        Assert.NotEqual(0, this.droppedFrames);

        await mx1.DisposeAsync();
        await mx2.DisposeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => Task.WhenAll(relay1, relay2)).NoThrowAwaitable();
    }

    /// <summary>
    /// Copies self-delimiting msgpack frames from one stream to another, dropping any frame whose control
    /// code is one an older peer would not recognize.
    /// </summary>
    private async Task RelayAsync(Stream from, Stream to, CancellationToken cancellationToken)
    {
        // These are the control codes added for window tuning. They are named here rather than referenced
        // because the enum is internal, and because an older peer would know nothing of them either.
        const int ChannelWindowAdjust = 7;
        const int ChannelWindowGrowthRequest = 8;

        using var reader = new MessagePackStreamReader(from);
        while (await reader.ReadAsync(cancellationToken) is ReadOnlySequence<byte> frame)
        {
            var peek = new MessagePackReader(frame);
            peek.ReadArrayHeader();
            int code = peek.ReadInt32();
            if (code is ChannelWindowAdjust or ChannelWindowGrowthRequest)
            {
                Interlocked.Increment(ref this.droppedFrames);
                this.Logger.WriteLine("Dropping frame with control code {0}.", code);
                continue;
            }

            byte[] bytes = frame.ToArray();
            await to.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            await to.FlushAsync(cancellationToken);
        }
    }
}
