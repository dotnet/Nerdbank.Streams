// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class MultiplexingStreamTests : TestBase, IAsyncLifetime
{
    private Stream transport1;
    private Stream transport2;
    private MultiplexingStream mx1;
    private MultiplexingStream mx2;

    public MultiplexingStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    public async Task InitializeAsync()
    {
        var mx1TraceSource = new TraceSource(nameof(this.mx1), SourceLevels.All);
        var mx2TraceSource = new TraceSource(nameof(this.mx2), SourceLevels.All);

        mx1TraceSource.Listeners.Add(new XunitTraceListener(this.Logger, nameof(this.mx1)));
        mx2TraceSource.Listeners.Add(new XunitTraceListener(this.Logger, nameof(this.mx2)));

        (this.transport1, this.transport2) = FullDuplexStream.CreateStreams();
        var mx1 = MultiplexingStream.CreateAsync(this.transport1, new MultiplexingStream.Options { TraceSource = mx1TraceSource }, this.TimeoutToken);
        var mx2 = MultiplexingStream.CreateAsync(this.transport2, new MultiplexingStream.Options { TraceSource = mx2TraceSource }, this.TimeoutToken);
        this.mx1 = await mx1;
        this.mx2 = await mx2;
    }

    public Task DisposeAsync()
    {
        this.mx1?.Dispose();
        this.mx2?.Dispose();
        AssertNoFault(this.mx1);
        AssertNoFault(this.mx2);

        this.mx1?.TraceSource.Listeners.OfType<XunitTraceListener>().SingleOrDefault()?.Dispose();
        this.mx2?.TraceSource.Listeners.OfType<XunitTraceListener>().SingleOrDefault()?.Dispose();

        return TplExtensions.CompletedTask;
    }

    [Fact]
    public async Task Dispose_CancelsOutstandingOperations()
    {
        Task offer = this.mx1.OfferChannelAsync("offer");
        Task accept = this.mx1.AcceptChannelAsync("accept");
        this.mx1.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(offer, accept)).WithCancellation(this.TimeoutToken);
        Assert.True(offer.IsCanceled);
        Assert.True(accept.IsCanceled);
    }

    [Fact]
    public void Disposal_DisposesTransportStream()
    {
        this.mx1.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.transport1.Position);
    }

    [Fact]
    public async Task Dispose_DisposesChannels()
    {
        var (channel1, channel2) = await this.EstablishChannelsAsync("A");
        this.mx1.Dispose();
        Assert.True(channel1.IsDisposed);
    }

    [Fact]
    public async Task CreateChannelAsync_ThrowsAfterDisposal()
    {
        this.mx1.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task AcceptChannelAsync_ThrowsAfterDisposal()
    {
        this.mx1.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.mx1.AcceptChannelAsync(string.Empty, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public void Completion_CompletedAfterDisposal()
    {
        this.mx1.Dispose();
        Assert.Equal(TaskStatus.RanToCompletion, this.mx1.Completion.Status);
    }

    [Fact]
    public async Task CreateChannelAsync_NullId()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => this.mx1.OfferChannelAsync(null, this.TimeoutToken));
    }

    [Fact]
    public async Task AcceptChannelAsync_NullId()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => this.mx1.AcceptChannelAsync(null, this.TimeoutToken));
    }

    [Fact]
    public async Task CreateChannelAsync_EmptyId()
    {
        var stream2Task = this.mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        var channel1 = await this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        var channel2 = await stream2Task.WithCancellation(this.TimeoutToken);
        Assert.NotNull(channel1);
        Assert.NotNull(channel2);
    }

    [Fact]
    public async Task CreateChannelAsync_CanceledBeforeAcceptance()
    {
        var cts = new CancellationTokenSource();
        var channel1Task = this.mx1.OfferChannelAsync("1st", cts.Token);
        Assert.False(channel1Task.IsCompleted);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel1Task).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task CreateChannelAsync()
    {
        await this.EstablishChannelStreamsAsync("a");
    }

    [Fact]
    public async Task CreateChannelAsync_TwiceWithDifferentCapitalization()
    {
        var (channel1a, channel1b) = await this.EstablishChannelStreamsAsync("a");
        var (channel2a, channel2b) = await this.EstablishChannelStreamsAsync("A");
        Assert.Equal(4, new[] { channel1a, channel1b, channel2a, channel2b }.Distinct().Count());
    }

    [Fact]
    public async Task CreateChannelAsync_IdCollidesWithPendingRequest()
    {
        var channel1aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);
        var channel2aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);

        var channel1b = await this.mx2.AcceptChannelAsync("1st", this.TimeoutToken).WithCancellation(this.TimeoutToken);
        var channel2b = await this.mx2.AcceptChannelAsync("1st", this.TimeoutToken).WithCancellation(this.TimeoutToken);

        await Task.WhenAll(channel1aTask, channel2aTask).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task CreateChannelAsync_IdCollidesWithExistingChannel()
    {
        for (int i = 0; i < 10; i++)
        {
            var channel1aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);
            var channel1bTask = this.mx2.AcceptChannelAsync("1st", this.TimeoutToken);
            await Task.WhenAll(channel1aTask, channel1bTask).WithCancellation(this.TimeoutToken);
        }
    }

    [Fact]
    public async Task CreateChannelAsync_IdRecycledFromPriorChannel()
    {
        var channel1aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);
        var channel1bTask = this.mx2.AcceptChannelAsync("1st", this.TimeoutToken);
        var channels = await Task.WhenAll(channel1aTask, channel1bTask).WithCancellation(this.TimeoutToken);
        channels[0].Dispose();
        channels[1].Dispose();

        channel1aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);
        channel1bTask = this.mx2.AcceptChannelAsync("1st", this.TimeoutToken);
        channels = await Task.WhenAll(channel1aTask, channel1bTask).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task CreateChannelAsync_AcceptByAnotherId()
    {
        var createTask = this.mx1.OfferChannelAsync("1st", ExpectedTimeoutToken);
        var acceptTask = this.mx2.AcceptChannelAsync("2nd", ExpectedTimeoutToken);
        Assert.False(createTask.IsCompleted);
        Assert.False(acceptTask.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => createTask).WithCancellation(this.TimeoutToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task CommunicateOverOneChannel()
    {
        var (a, b) = await this.EstablishChannelStreamsAsync("a");
        await this.TransmitAndVerifyAsync(a, b, Guid.NewGuid().ToByteArray());
        await this.TransmitAndVerifyAsync(b, a, Guid.NewGuid().ToByteArray());
    }

    [Fact]
    [Trait("SkipInCodeCoverage", "true")] // far too slow and times out
    public async Task ConcurrentChatOverManyChannels()
    {
        // Avoid tracing because it slows things down significantly for this test.
        this.mx1.TraceSource.Switch.Level = SourceLevels.Error;
        this.mx2.TraceSource.Switch.Level = SourceLevels.Error;

        const int channels = 10;
        const int iterations = 1000;
        await Task.WhenAll(Enumerable.Range(1, channels).Select(i => CoordinateChatAsync()));

        async Task CoordinateChatAsync()
        {
            var (a, b) = await this.EstablishChannelStreamsAsync("chat").WithCancellation(this.TimeoutToken);
            var messageA = Guid.NewGuid().ToByteArray();
            var messageB = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
            await Task.WhenAll(
                Task.Run(() => ChatAsync(a, messageA, messageB)),
                Task.Run(() => ChatAsync(b, messageB, messageA)));
        }

        async Task ChatAsync(Stream s, byte[] send, byte[] receive)
        {
            byte[] recvBuffer = new byte[receive.Length];
            for (int i = 0; i < iterations; i++)
            {
                await s.WriteAsync(send, 0, send.Length).WithCancellation(this.TimeoutToken);
                await s.FlushAsync(this.TimeoutToken).WithCancellation(this.TimeoutToken);
                Assert.Equal(recvBuffer.Length, await ReadAtLeastAsync(s, new ArraySegment<byte>(recvBuffer), recvBuffer.Length, this.TimeoutToken));
                Assert.Equal(receive, recvBuffer);
            }
        }
    }

    [Fact]
    public async Task ReadReturns0AfterRemoteEnd()
    {
        var (a, b) = await this.EstablishChannelStreamsAsync("a");
        a.Dispose();
        var buffer = new byte[1];
        Assert.Equal(0, await b.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken));
        Assert.Equal(0, b.Read(buffer, 0, buffer.Length));
        Assert.Equal(-1, b.ReadByte());
    }

    //// TODO: add test where the locally transmitting pipe is closed, the remote detects this, sends one more message, closes their end, and the channels close as the last message is sent and received.

    [Fact]
    public async Task ReadByte()
    {
        var (a, b) = await this.EstablishChannelStreamsAsync("a");
        var buffer = new byte[] { 5 };
        await a.WriteAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        await a.FlushAsync(this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(5, b.ReadByte());
    }

    [Fact]
    [Trait("SkipInCodeCoverage", "true")]
    public async Task TransmitAndCloseChannel()
    {
        var buffer = new byte[1024 * 1024];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0xcc;
        }

        var (a, b) = await this.EstablishChannelStreamsAsync("a");
        await a.WriteAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        await a.FlushAsync(this.TimeoutToken);
        a.Dispose();

        var receivingBuffer = new byte[(1024 * 1024) + 1];
        int readBytes = await ReadAtLeastAsync(b, new ArraySegment<byte>(receivingBuffer), buffer.Length, this.TimeoutToken);
        Assert.Equal(buffer.Length, readBytes);
        Assert.Equal(buffer, receivingBuffer.Take(buffer.Length));

        Assert.Equal(0, await b.ReadAsync(receivingBuffer, 0, 1, this.TimeoutToken).WithCancellation(this.TimeoutToken));
    }

    [Fact]
    public async Task WriteLargeBuffer()
    {
        var sendBuffer = new byte[1024 * 1024];
        var random = new Random();
        random.NextBytes(sendBuffer);
        var (a, b) = await this.EstablishChannelStreamsAsync("a");
        await a.WriteAsync(sendBuffer, 0, sendBuffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        await a.FlushAsync(this.TimeoutToken).WithCancellation(this.TimeoutToken);

        var recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(b, recvBuffer);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Fact]
    public async Task CanProperties()
    {
        var (s1, s2) = await this.EstablishChannelStreamsAsync(string.Empty);
        Assert.False(s1.CanSeek);
        Assert.True(s1.CanWrite);
        Assert.True(s1.CanRead);
        s1.Dispose();
        Assert.False(s1.CanSeek);
        Assert.False(s1.CanWrite);
        Assert.False(s1.CanRead);
    }

    [Fact]
    public async Task NotSupportedMethodsAndProperties()
    {
        var (s1, s2) = await this.EstablishChannelStreamsAsync(string.Empty);
        Assert.Throws<NotSupportedException>(() => s1.Length);
        Assert.Throws<NotSupportedException>(() => s1.Position);
        Assert.Throws<NotSupportedException>(() => s1.Position = 0);
        Assert.Throws<NotSupportedException>(() => s1.SetLength(0));
        Assert.Throws<NotSupportedException>(() => s1.Seek(0, SeekOrigin.Begin));
        s1.Dispose();
        Assert.Throws<ObjectDisposedException>(() => s1.Length);
        Assert.Throws<ObjectDisposedException>(() => s1.Position);
        Assert.Throws<ObjectDisposedException>(() => s1.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => s1.SetLength(0));
        Assert.Throws<ObjectDisposedException>(() => s1.Seek(0, SeekOrigin.Begin));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialFrameSentOnlyOnFlush(bool flushAsync)
    {
        var (s1, s2) = await this.EstablishChannelStreamsAsync(string.Empty);

        byte[] smallData = new byte[] { 0x1, 0x2, 0x3 };
        await s1.WriteAsync(smallData, 0, smallData.Length).WithCancellation(this.TimeoutToken);
        byte[] recvBuffer = new byte[smallData.Length];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s2.ReadAsync(recvBuffer, 0, recvBuffer.Length, ExpectedTimeoutToken));

        if (flushAsync)
        {
            await s1.FlushAsync();
        }
        else
        {
            s1.Flush();
        }

        await ReadAtLeastAsync(s2, new ArraySegment<byte>(recvBuffer), recvBuffer.Length, this.TimeoutToken);
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelChannelOfferBeforeAcceptance(bool cancelFirst)
    {
        // TODO: We need to test both the race condition where acceptance is sent before cancellation is received,
        //       and the case where cancellation is received before we call AcceptChannelAsync.
        var cts = new CancellationTokenSource();
        var offer = this.mx1.OfferChannelAsync(string.Empty, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => offer).WithCancellation(this.TimeoutToken);
        Stream acceptedStream = null;
        try
        {
            if (cancelFirst)
            {
                // Increase the odds that cancellation will be processed before acceptance.
                await Task.Delay(250);
            }

            var acceptedChannel = await this.mx2.AcceptChannelAsync(string.Empty, ExpectedTimeoutToken).ConfigureAwait(false);
            acceptedStream = acceptedChannel.AsStream();

            // In this case, we accepted the channel before receiving the cancellation notice. The channel should be terminated by the remote side very soon.
            int bytesRead = await acceptedStream.ReadAsync(new byte[1], 0, 1, this.TimeoutToken).WithCancellation(this.TimeoutToken);
            Assert.Equal(0, bytesRead); // confirm that the stream was closed.
            this.Logger.WriteLine("Verified the channel terminated condition.");
            Skip.If(cancelFirst);
        }
        catch (OperationCanceledException) when (acceptedStream == null)
        {
            // In this case, the channel offer was canceled before we accepted it.
            this.Logger.WriteLine("Verified the channel offer was canceled before acceptance condition.");
            Skip.IfNot(cancelFirst);
        }
    }

    [Fact]
    public async Task EphemeralChannels()
    {
        var ephemeralMessage = new byte[10];
        var random = new Random();
        random.NextBytes(ephemeralMessage);

        await Task.WhenAll(
            Task.Run(async delegate
            {
                var rpcChannel = await this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken);
                var eph = this.mx1.CreateChannel();
                await rpcChannel.Output.WriteAsync(BitConverter.GetBytes(eph.Id), this.TimeoutToken);
                await eph.Output.WriteAsync(ephemeralMessage, this.TimeoutToken);
                await eph.Acceptance;
            }),
            Task.Run(async delegate
            {
                var rpcChannel = await this.mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken);
                var buffer = new byte[ephemeralMessage.Length];
                var readResult = await ReadAtLeastAsync(rpcChannel.AsStream(), new ArraySegment<byte>(buffer), sizeof(int), this.TimeoutToken);
                int channelId = BitConverter.ToInt32(buffer, 0);
                var eph = this.mx2.AcceptChannel(channelId);
                Assert.True(eph.Acceptance.IsCompleted);
                readResult = await ReadAtLeastAsync(eph.AsStream(), new ArraySegment<byte>(buffer), ephemeralMessage.Length, this.TimeoutToken);
                Assert.Equal(ephemeralMessage, buffer);
            }));
    }

    [Fact]
    public async Task EphemeralChannels_AcceptTwice_Throws()
    {
        await Task.WhenAll(
            Task.Run(async delegate
            {
                var rpcChannel = await this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken);
                var eph = this.mx1.CreateChannel();
                await rpcChannel.Output.WriteAsync(BitConverter.GetBytes(eph.Id), this.TimeoutToken);
                await eph.Acceptance;
            }),
            Task.Run(async delegate
            {
                var buffer = new byte[sizeof(int)];
                var rpcChannel = await this.mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken);
                var readResult = await ReadAtLeastAsync(rpcChannel.AsStream(), new ArraySegment<byte>(buffer), sizeof(int), this.TimeoutToken);
                int channelId = BitConverter.ToInt32(buffer, 0);
                var eph = this.mx2.AcceptChannel(channelId);
                Assert.Throws<InvalidOperationException>(() => this.mx2.AcceptChannel(channelId));
            }));
    }

    [Fact]
    public async Task EphemeralChannels_Rejected()
    {
        await Task.WhenAll(
            Task.Run(async delegate
            {
                var rpcChannel = await this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken);
                var eph = this.mx1.CreateChannel();
                await rpcChannel.Output.WriteAsync(BitConverter.GetBytes(eph.Id), this.TimeoutToken);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => eph.Acceptance).WithCancellation(this.TimeoutToken);
            }),
            Task.Run(async delegate
            {
                var buffer = new byte[sizeof(int)];
                var rpcChannel = await this.mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken);
                var readResult = await ReadAtLeastAsync(rpcChannel.AsStream(), new ArraySegment<byte>(buffer), sizeof(int), this.TimeoutToken);
                int channelId = BitConverter.ToInt32(buffer, 0);
                this.mx2.RejectChannel(channelId);

                // At this point, it's too late to accept
                Assert.Throws<InvalidOperationException>(() => this.mx2.AcceptChannel(channelId));
            }));
    }

    [Fact]
    public void AcceptChannel_NeverExisted()
    {
        Assert.Throws<InvalidOperationException>(() => this.mx1.AcceptChannel(15));
    }

    private static async Task<int> ReadAtLeastAsync(Stream stream, ArraySegment<byte> buffer, int requiredLength, CancellationToken cancellationToken)
    {
        Requires.NotNull(stream, nameof(stream));
        Requires.NotNull(buffer.Array, nameof(buffer));
        Requires.Range(requiredLength >= 0, nameof(requiredLength));

        int bytesRead = 0;
        while (bytesRead < requiredLength)
        {
            int bytesReadJustNow = await stream.ReadAsync(buffer.Array, buffer.Offset + bytesRead, buffer.Count - bytesRead, cancellationToken).ConfigureAwait(false);
            Assert.NotEqual(0, bytesReadJustNow);
            bytesRead += bytesReadJustNow;
        }

        return bytesRead;
    }

    private static void AssertNoFault(MultiplexingStream stream)
    {
        Exception fault = stream?.Completion.Exception?.InnerException;
        if (fault != null)
        {
            ExceptionDispatchInfo.Capture(fault).Throw();
        }
    }

    private async Task TransmitAndVerifyAsync(Stream writeTo, Stream readFrom, byte[] data)
    {
        Requires.NotNull(writeTo, nameof(writeTo));
        Requires.NotNull(readFrom, nameof(readFrom));
        Requires.NotNull(data, nameof(data));

        await writeTo.WriteAsync(data, 0, data.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        await writeTo.FlushAsync().WithCancellation(this.TimeoutToken);
        var readBuffer = new byte[data.Length * 2];
        int readBytes = await ReadAtLeastAsync(readFrom, new ArraySegment<byte>(readBuffer), data.Length, this.TimeoutToken);
        Assert.Equal(data.Length, readBytes);
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(data[i], readBuffer[i]);
        }
    }

    private async Task<(MultiplexingStream.Channel, MultiplexingStream.Channel)> EstablishChannelsAsync(string identifier)
    {
        var mx1ChannelTask = this.mx1.OfferChannelAsync(identifier, this.TimeoutToken);
        var mx2ChannelTask = this.mx2.AcceptChannelAsync(identifier, this.TimeoutToken);
        var channels = await Task.WhenAll(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);
        Assert.NotNull(channels[0]);
        Assert.NotNull(channels[1]);
        return (channels[0], channels[1]);
    }

    private async Task<(Stream, Stream)> EstablishChannelStreamsAsync(string identifier)
    {
        var (channel1, channel2) = await this.EstablishChannelsAsync(identifier);
        return (channel1.AsStream(), channel2.AsStream());
    }
}
