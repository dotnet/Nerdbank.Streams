// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1401 // Fields should be private
#pragma warning disable SA1414 // Tuple types in signatures should have element names

public class MultiplexingStreamTests : TestBase, IAsyncLifetime
{
    protected Stream transport1;
    protected Stream transport2;
    protected MultiplexingStream mx1;
    protected MultiplexingStream mx2;

#pragma warning disable CS8618 // Fields initialized in InitializeAsync
    public MultiplexingStreamTests(ITestOutputHelper logger)
#pragma warning restore CS8618 // Fields initialized in InitializeAsync
        : base(logger)
    {
    }

    protected virtual int ProtocolMajorVersion { get; } = 1;

    public async Task InitializeAsync()
    {
        var mx1TraceSource = new TraceSource(nameof(this.mx1), SourceLevels.All);
        var mx2TraceSource = new TraceSource(nameof(this.mx2), SourceLevels.All);

        mx1TraceSource.Listeners.Add(new XunitTraceListener(this.Logger, this.TestId, this.TestTimer));
        mx2TraceSource.Listeners.Add(new XunitTraceListener(this.Logger, this.TestId, this.TestTimer));

        Func<string, MultiplexingStream.QualifiedChannelId, string, TraceSource> traceSourceFactory = (string mxInstanceName, MultiplexingStream.QualifiedChannelId id, string name) =>
        {
            var traceSource = new TraceSource(mxInstanceName + " channel " + id, SourceLevels.All);
            traceSource.Listeners.Clear(); // remove DefaultTraceListener
            traceSource.Listeners.Add(new XunitTraceListener(this.Logger, this.TestId, this.TestTimer));
            return traceSource;
        };

        Func<MultiplexingStream.QualifiedChannelId, string, TraceSource> mx1TraceSourceFactory = (MultiplexingStream.QualifiedChannelId id, string name) => traceSourceFactory(nameof(this.mx1), id, name);
        Func<MultiplexingStream.QualifiedChannelId, string, TraceSource> mx2TraceSourceFactory = (MultiplexingStream.QualifiedChannelId id, string name) => traceSourceFactory(nameof(this.mx2), id, name);

        (this.transport1, this.transport2) = FullDuplexStream.CreatePair(new PipeOptions(pauseWriterThreshold: 2 * 1024 * 1024));
        Task<MultiplexingStream>? mx1 = MultiplexingStream.CreateAsync(this.transport1, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion, TraceSource = mx1TraceSource, DefaultChannelTraceSourceFactoryWithQualifier = mx1TraceSourceFactory }, this.TimeoutToken);
        Task<MultiplexingStream>? mx2 = MultiplexingStream.CreateAsync(this.transport2, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion, TraceSource = mx2TraceSource, DefaultChannelTraceSourceFactoryWithQualifier = mx2TraceSourceFactory }, this.TimeoutToken);
        this.mx1 = await mx1;
        this.mx2 = await mx2;
    }

    public async Task DisposeAsync()
    {
        await (this.mx1?.DisposeAsync() ?? default);
        await (this.mx2?.DisposeAsync() ?? default);
        AssertNoFault(this.mx1);
        AssertNoFault(this.mx2);

        this.mx1?.TraceSource.Listeners.OfType<XunitTraceListener>().SingleOrDefault()?.Dispose();
        this.mx2?.TraceSource.Listeners.OfType<XunitTraceListener>().SingleOrDefault()?.Dispose();
    }

    [Fact, Obsolete]
    public async Task DefaultChannelTraceSourceFactory()
    {
        var factoryArgs = new TaskCompletionSource<(int, string)>();
        var obsoleteFactory = new Func<int, string, TraceSource?>((id, name) =>
        {
            factoryArgs.SetResult((id, name));
            return null;
        });
        (this.transport1, this.transport2) = FullDuplexStream.CreatePair();
        Task<MultiplexingStream>? mx1Task = MultiplexingStream.CreateAsync(this.transport1, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion, DefaultChannelTraceSourceFactory = obsoleteFactory }, this.TimeoutToken);
        Task<MultiplexingStream>? mx2Task = MultiplexingStream.CreateAsync(this.transport2, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion }, this.TimeoutToken);
        MultiplexingStream? mx1 = await mx1Task;
        MultiplexingStream? mx2 = await mx2Task;

        MultiplexingStream.Channel[]? ch = await Task.WhenAll(mx1.OfferChannelAsync("myname"), mx2.AcceptChannelAsync("myname"));
        (int, string) args = await factoryArgs.Task;
        Assert.Equal(ch[0].QualifiedId.Id, (ulong)args.Item1);
        Assert.Equal("myname", args.Item2);
    }

    [Fact]
    public void DefaultMajorProtocolVersion()
    {
        Assert.Equal(1, new MultiplexingStream.Options().ProtocolMajorVersion);
    }

    [Fact]
    public async Task OfferReadOnlyDuplexPipe()
    {
        // Prepare a readonly pipe that is already fully populated with data for the other end to read.
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 1, 2, 3 }, this.TimeoutToken);
        pipe.Writer.Complete();

        MultiplexingStream.Channel? ch1 = this.mx1.CreateChannel(new MultiplexingStream.ChannelOptions { ExistingPipe = new DuplexPipe(pipe.Reader) });
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        MultiplexingStream.Channel? ch2 = this.mx2.AcceptChannel(ch1.QualifiedId.Id);
        ReadResult readResult = await ch2.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(3, readResult.Buffer.Length);
        ch2.Input.AdvanceTo(readResult.Buffer.End);
        readResult = await ch2.Input.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);
        ch2.Output.Complete();

        await Task.WhenAll(ch1.Completion, ch2.Completion).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task OfferReadOnlyPipe()
    {
        // Prepare a readonly pipe that is already fully populated with data for the other end to read.
        (IDuplexPipe, IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();
        pipePair.Item1.Input.Complete(); // we don't read -- we only write.
        await pipePair.Item1.Output.WriteAsync(new byte[] { 1, 2, 3 }, this.TimeoutToken);
        pipePair.Item1.Output.Complete();

        MultiplexingStream.Channel? ch1 = this.mx1.CreateChannel(new MultiplexingStream.ChannelOptions { ExistingPipe = pipePair.Item2 });
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        MultiplexingStream.Channel? ch2 = this.mx2.AcceptChannel(ch1.QualifiedId.Id);
        ReadResult readResult = await ch2.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(3, readResult.Buffer.Length);
        ch2.Input.AdvanceTo(readResult.Buffer.End);
        readResult = await ch2.Input.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);
        ch2.Output.Complete();

        await Task.WhenAll(ch1.Completion, ch2.Completion).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task OfferWriteOnlyDuplexPipe()
    {
        var pipe = new Pipe();

        MultiplexingStream.Channel? ch1 = this.mx1.CreateChannel(new MultiplexingStream.ChannelOptions { ExistingPipe = new DuplexPipe(pipe.Writer) });
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        MultiplexingStream.Channel? ch2 = this.mx2.AcceptChannel(ch1.QualifiedId.Id);

        // Confirm that any attempt to read from the channel is immediately completed.
        ReadResult readResult = await ch2.Input.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);

        // Now write to the channel.
        await ch2.Output.WriteAsync(new byte[] { 1, 2, 3 }, this.TimeoutToken);
        ch2.Output.Complete();

        readResult = await pipe.Reader.ReadAsync(this.TimeoutToken);
        Assert.Equal(3, readResult.Buffer.Length);
        pipe.Reader.AdvanceTo(readResult.Buffer.End);
        readResult = await pipe.Reader.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);

        await Task.WhenAll(ch1.Completion, ch2.Completion).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task OfferWriteOnlyPipe()
    {
        (IDuplexPipe, IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();
        pipePair.Item1.Output.Complete(); // we don't write -- we only read.

        MultiplexingStream.Channel? ch1 = this.mx1.CreateChannel(new MultiplexingStream.ChannelOptions { ExistingPipe = pipePair.Item2 });
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        MultiplexingStream.Channel? ch2 = this.mx2.AcceptChannel(ch1.QualifiedId.Id);

        // Confirm that any attempt to read from the channel is immediately completed.
        ReadResult readResult = await ch2.Input.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);

        // Now write to the channel.
        await ch2.Output.WriteAsync(new byte[] { 1, 2, 3 }, this.TimeoutToken);
        ch2.Output.Complete();

        readResult = await pipePair.Item1.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(3, readResult.Buffer.Length);
        pipePair.Item1.Input.AdvanceTo(readResult.Buffer.End);
        readResult = await pipePair.Item1.Input.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);

        await Task.WhenAll(ch1.Completion, ch2.Completion).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task Dispose_CancelsOutstandingOperations()
    {
        Task offer = this.mx1.OfferChannelAsync("offer");
        Task accept = this.mx1.AcceptChannelAsync("accept");
        await this.mx1.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(offer, accept)).WithCancellation(this.TimeoutToken);
        Assert.True(offer.IsCanceled);
        Assert.True(accept.IsCanceled);
    }

    [Fact]
    public async Task Disposal_DisposesTransportStream()
    {
        await this.mx1.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => this.transport1.Position);
    }

    [Fact]
    public async Task Dispose_DisposesChannels()
    {
        (MultiplexingStream.Channel channel1, MultiplexingStream.Channel channel2) = await this.EstablishChannelsAsync("A");
        await this.mx1.DisposeAsync();
        Assert.True(channel1.IsDisposed);
        await channel1.Completion.WithCancellation(this.TimeoutToken);
#pragma warning disable CS0618 // Type or member is obsolete
        await channel1.Input.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
        await channel1.Output.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    [Fact]
    public async Task ChannelDispose_ClosesExistingStream()
    {
        var ms = new MonitoringStream(FullDuplexStream.CreatePair().Item1);
        var disposal = new AsyncManualResetEvent();
        ms.Disposed += (s, e) => disposal.Set();

        MultiplexingStream.Channel? channel = this.mx1.CreateChannel(new MultiplexingStream.ChannelOptions { ExistingPipe = ms.UsePipe() });
        channel.Dispose();
        await disposal.WaitAsync(this.TimeoutToken);
    }

    [Fact]
    public async Task RemoteChannelClose_ClosesExistingStream()
    {
        var ms = new MonitoringStream(FullDuplexStream.CreatePair().Item1);
        var disposal = new AsyncManualResetEvent();
        ms.Disposed += (s, e) => disposal.Set();

        MultiplexingStream.Channel? ch1 = this.mx1.CreateChannel(new MultiplexingStream.ChannelOptions { ExistingPipe = ms.UsePipe() });
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        MultiplexingStream.Channel? ch2 = this.mx2.AcceptChannel(ch1.QualifiedId.Id);

        ch2.Dispose();
        await disposal.WaitAsync(this.TimeoutToken);
    }

    [Fact]
    public async Task CreateChannelAsync_ThrowsAfterDisposal()
    {
        await this.mx1.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task AcceptChannelAsync_ThrowsAfterDisposal()
    {
        await this.mx1.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.mx1.AcceptChannelAsync(string.Empty, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task Completion_CompletedAfterDisposal()
    {
        await this.mx1.DisposeAsync();
        Assert.Equal(TaskStatus.RanToCompletion, this.mx1.Completion.Status);
    }

    [Fact]
    public async Task CreateChannelAsync_NullId()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => this.mx1.OfferChannelAsync(null!, this.TimeoutToken));
    }

    [Fact]
    public async Task AcceptChannelAsync_NullId()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => this.mx1.AcceptChannelAsync(null!, this.TimeoutToken));
    }

    [Fact]
    public async Task CreateChannelAsync_EmptyId()
    {
        Task<MultiplexingStream.Channel>? stream2Task = this.mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        MultiplexingStream.Channel? channel1 = await this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        MultiplexingStream.Channel? channel2 = await stream2Task.WithCancellation(this.TimeoutToken);
        Assert.NotNull(channel1);
        Assert.NotNull(channel2);
    }

    [Fact]
    public async Task CreateChannelAsync_CanceledBeforeAcceptance()
    {
        var cts = new CancellationTokenSource();
        Task<MultiplexingStream.Channel>? channel1Task = this.mx1.OfferChannelAsync("1st", cts.Token);
        Assert.False(channel1Task.IsCompleted);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel1Task).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task CreateAsync_CancellationToken()
    {
        var cts = new CancellationTokenSource();
        (Stream, Stream) streamPair = FullDuplexStream.CreatePair();
        Task<MultiplexingStream> mx1Task = MultiplexingStream.CreateAsync(streamPair.Item1, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion }, cts.Token);
        Task<MultiplexingStream> mx2Task = MultiplexingStream.CreateAsync(streamPair.Item2, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion }, CancellationToken.None);
        MultiplexingStream mx1 = await mx1Task;
        MultiplexingStream mx2 = await mx2Task;

        // At this point the cancellation token really shouldn't have any effect on mx1 now that the connection is established.
        cts.Cancel();

        Task<MultiplexingStream.Channel> ch1Task = mx1.OfferChannelAsync(string.Empty, this.TimeoutToken);
        Task<MultiplexingStream.Channel> ch2Task = mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken);

        await Task.WhenAll(ch1Task, ch2Task).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task CreateChannelAsync()
    {
        await this.EstablishChannelStreamsAsync("a");
    }

    [Fact]
    public async Task CreateChannelAsync_TwiceWithDifferentCapitalization()
    {
        (Stream channel1a, Stream channel1b) = await this.EstablishChannelStreamsAsync("a");
        (Stream channel2a, Stream channel2b) = await this.EstablishChannelStreamsAsync("A");
        Assert.Equal(4, new[] { channel1a, channel1b, channel2a, channel2b }.Distinct().Count());
    }

    [Fact]
    public async Task CreateChannelAsync_IdCollidesWithPendingRequest()
    {
        Task<MultiplexingStream.Channel>? channel1aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);
        Task<MultiplexingStream.Channel>? channel2aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);

        MultiplexingStream.Channel? channel1b = await this.mx2.AcceptChannelAsync("1st", this.TimeoutToken).WithCancellation(this.TimeoutToken);
        MultiplexingStream.Channel? channel2b = await this.mx2.AcceptChannelAsync("1st", this.TimeoutToken).WithCancellation(this.TimeoutToken);

        await Task.WhenAll(channel1aTask, channel2aTask).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task CreateChannelAsync_IdCollidesWithExistingChannel()
    {
        for (int i = 0; i < 10; i++)
        {
            Task<MultiplexingStream.Channel>? channel1aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);
            Task<MultiplexingStream.Channel>? channel1bTask = this.mx2.AcceptChannelAsync("1st", this.TimeoutToken);
            await Task.WhenAll(channel1aTask, channel1bTask).WithCancellation(this.TimeoutToken);
        }
    }

    [Fact]
    public async Task CreateChannelAsync_IdRecycledFromPriorChannel()
    {
        Task<MultiplexingStream.Channel>? channel1aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);
        Task<MultiplexingStream.Channel>? channel1bTask = this.mx2.AcceptChannelAsync("1st", this.TimeoutToken);
        MultiplexingStream.Channel[]? channels = await Task.WhenAll(channel1aTask, channel1bTask).WithCancellation(this.TimeoutToken);
        channels[0].Dispose();
        channels[1].Dispose();

        channel1aTask = this.mx1.OfferChannelAsync("1st", this.TimeoutToken);
        channel1bTask = this.mx2.AcceptChannelAsync("1st", this.TimeoutToken);
        channels = await Task.WhenAll(channel1aTask, channel1bTask).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task CreateChannelAsync_AcceptByAnotherId()
    {
        var cts = new CancellationTokenSource();
        Task<MultiplexingStream.Channel>? createTask = this.mx1.OfferChannelAsync("1st", cts.Token);
        Task<MultiplexingStream.Channel>? acceptTask = this.mx2.AcceptChannelAsync("2nd", cts.Token);
        Assert.False(createTask.IsCompleted);
        Assert.False(acceptTask.IsCompleted);
        cts.CancelAfter(ExpectedTimeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => createTask).WithCancellation(this.TimeoutToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public void ChannelExposesMultiplexingStream()
    {
        MultiplexingStream.Channel? channel = this.mx1.CreateChannel();
        Assert.Same(this.mx1, channel.MultiplexingStream);
        channel.Dispose();
        Assert.Same(this.mx1, channel.MultiplexingStream);
    }

    [Fact]
    public async Task CommunicateOverOneChannel()
    {
        (Stream a, Stream b) = await this.EstablishChannelStreamsAsync("a");
        await this.TransmitAndVerifyAsync(a, b, Guid.NewGuid().ToByteArray());
        await this.TransmitAndVerifyAsync(b, a, Guid.NewGuid().ToByteArray());
    }

    [Fact]
    public async Task ChannelWithExistingSimplexChannel()
    {
        var transmittingPipe = new Pipe();
        var receivingPipe = new Pipe();
        Task<MultiplexingStream.Channel>? mx1ChannelTask = this.mx1.OfferChannelAsync(string.Empty, new MultiplexingStream.ChannelOptions { ExistingPipe = new DuplexPipe(transmittingPipe.Reader) }, this.TimeoutToken);
        Task<MultiplexingStream.Channel>? mx2ChannelTask = this.mx2.AcceptChannelAsync(string.Empty, new MultiplexingStream.ChannelOptions { ExistingPipe = new DuplexPipe(receivingPipe.Writer) }, this.TimeoutToken);
        MultiplexingStream.Channel[]? channels = await WhenAllSucceedOrAnyFail(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);

        byte[]? buffer = this.GetBuffer(3);
        await transmittingPipe.Writer.WriteAsync(buffer, this.TimeoutToken);

        ReadOnlySequence<byte> readBytes = await this.ReadAtLeastAsync(receivingPipe.Reader, buffer.Length);
        Assert.Equal(buffer.Length, readBytes.Length);
        Assert.Equal(buffer, readBytes.ToArray());
    }

    [Fact]
    [Trait("SkipInCodeCoverage", "true")] // far too slow and times out
    [Trait("Stress", "true")]
    public async Task ConcurrentChatOverManyChannels()
    {
        // Avoid tracing because it slows things down significantly for this test.
        this.mx1.TraceSource.Switch.Level = SourceLevels.Error;
        this.mx2.TraceSource.Switch.Level = SourceLevels.Error;

        const int channels = 10;
        const int iterations = 500;
        await Task.WhenAll(Enumerable.Range(1, channels).Select(i => CoordinateChatAsync()));

        async Task CoordinateChatAsync()
        {
            (Stream a, Stream b) = await this.EstablishChannelStreamsAsync("chat").WithCancellation(this.TimeoutToken);
            byte[]? messageA = Guid.NewGuid().ToByteArray();
            byte[]? messageB = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
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
        (Stream a, Stream b) = await this.EstablishChannelStreamsAsync("a");
        a.Dispose();
        byte[]? buffer = new byte[1];
        Assert.Equal(0, await b.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken));
        Assert.Equal(0, b.Read(buffer, 0, buffer.Length));
        Assert.Equal(-1, b.ReadByte());
    }

    //// TODO: add test where the locally transmitting pipe is closed, the remote detects this, sends one more message, closes their end, and the channels close as the last message is sent and received.

    [Fact]
    public async Task ReadByte()
    {
        (Stream a, Stream b) = await this.EstablishChannelStreamsAsync("a");
        byte[]? buffer = new byte[] { 5 };
        await a.WriteAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        await a.FlushAsync(this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(5, b.ReadByte());
    }

    [Theory]
    [InlineData(1024 * 1024)]
    [InlineData(5)]
    [Trait("SkipInCodeCoverage", "true")]
    public async Task TransmitOverStreamAndDisposeStream(int length)
    {
        byte[]? buffer = this.GetBuffer(length);
        (Stream a, Stream b) = await this.EstablishChannelStreamsAsync("a");
        Task writerTask = Task.Run(async delegate
        {
            await a.WriteAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
            await a.FlushAsync(this.TimeoutToken);
            a.Dispose();
        });

        byte[]? receivingBuffer = new byte[length + 1];
        int readBytes = await ReadAtLeastAsync(b, new ArraySegment<byte>(receivingBuffer), buffer.Length, this.TimeoutToken);
        Assert.Equal(buffer.Length, readBytes);
        Assert.Equal(buffer, receivingBuffer.Take(buffer.Length));

        Assert.Equal(0, await b.ReadAsync(receivingBuffer, 0, 1, this.TimeoutToken).WithCancellation(this.TimeoutToken));

        await writerTask;
    }

    /// <summary>
    /// Verifies that writing to a <see cref="MultiplexingStream.Channel"/> (without an <see cref="MultiplexingStream.ChannelOptions.ExistingPipe"/>)
    /// and then immediately completing the writer still writes everything that was pending.
    /// </summary>
    [Theory]
    [InlineData(1024 * 1024)]
    [InlineData(5)]
    [Trait("SkipInCodeCoverage", "true")]
    public async Task TransmitOverPipeAndCompleteWriting(int length)
    {
        byte[]? buffer = this.GetBuffer(length);
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");
        await b.Output.CompleteAsync(); // we won't Write anything in this direction.
        Task writerTask = Task.Run(async delegate
        {
            await a.Output.WriteAsync(buffer, this.TimeoutToken);
            await a.Output.CompleteAsync();
        });

        ReadOnlySequence<byte> readBytes = await this.ReadAtLeastAsync(b.Input, length);
        Assert.Equal(buffer.Length, readBytes.Length);
        Assert.Equal(buffer, readBytes.ToArray());

#pragma warning disable CS0618 // Type or member is obsolete
        await b.Input.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete

        await writerTask;
    }

    /// <summary>
    /// Verifies that writing to a <see cref="MultiplexingStream.Channel"/> (with an <see cref="MultiplexingStream.ChannelOptions.ExistingPipe"/>)
    /// and then immediately disposing the channel still writes everything that was pending.
    /// </summary>
    [Theory]
    [InlineData(1024 * 1024)]
    [InlineData(5)]
    [Trait("SkipInCodeCoverage", "true")]
    public async Task TransmitOverPreexistingPipeAndDisposeChannel(int length)
    {
        byte[]? buffer = this.GetBuffer(length);
        (IDuplexPipe, IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();

        const string channelName = "a";
        Task<MultiplexingStream.Channel>? mx1ChannelTask = this.mx1.OfferChannelAsync(channelName, new MultiplexingStream.ChannelOptions { ExistingPipe = pipePair.Item1 }, this.TimeoutToken);
        Task<MultiplexingStream.Channel>? mx2ChannelTask = this.mx2.AcceptChannelAsync(channelName, this.TimeoutToken);
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await Task.WhenAll(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);

        Task writerTask = Task.Run(async delegate
        {
            await pipePair.Item2.Output.WriteAsync(buffer, this.TimeoutToken);
            a.Dispose();
        });

        // In this scenario, there is actually no guarantee that bytes written previously were transmitted before the Channel was disposed.
        // In practice, folks with ExistingPipe set should complete their writer instead of disposing so the channel can dispose when writing (on both sides) is done.
        // In order for the b stream to recognize the closure, it does need to have read all the bytes that *were* transmitted though,
        // so drain the pipe insofar as it has bytes. Just don't assert how many were read.
        await this.DrainReaderTillCompletedAsync(b.Input);

#pragma warning disable CS0618 // Type or member is obsolete
        await b.Input.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete

        await writerTask;
    }

    /// <summary>
    /// Verifies that disposing a <see cref="MultiplexingStream.Channel"/> that is still receiving data from the remote party
    /// causes such received data to be silently dropped.
    /// </summary>
    [Theory]
    [InlineData(1024 * 1024)]
    [InlineData(5)]
    [Trait("SkipInCodeCoverage", "true")]
    public async Task DisposeChannel_WhileRemoteEndIsTransmitting(int length)
    {
        byte[]? buffer = this.GetBuffer(length);
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        // We don't await this because with large input sizes it can't complete faster than the reader is reading it.
        ValueTask<FlushResult> writerTask = a.Output.WriteAsync(buffer, this.TimeoutToken);

        // While the prior transmission is going, dispose the channel on the receiving end.
        b.Dispose();

        // Prove that communication between the two streams is still possible.
        // This is interesting particularly when the amount of data transmitted is high and would back up the pipes
        // such that the reader would stop if the disposed channel's content were not being actively discarded.
        await this.EstablishChannelsAsync("b");

        // Confirm the writer task completes.
        // Although the receiving end was disposed, we should never throw on the transmission end as a result of that.
        await writerTask;
    }

    [Fact]
    public async Task WriteLargeBuffer()
    {
        byte[]? sendBuffer = new byte[1024 * 1024];
        var random = new Random();
        random.NextBytes(sendBuffer);
        (Stream a, Stream b) = await this.EstablishChannelStreamsAsync("a");
        Task writeAndFlush = Task.Run(async delegate
        {
            await a.WriteAsync(sendBuffer, 0, sendBuffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
            await a.FlushAsync(this.TimeoutToken).WithCancellation(this.TimeoutToken);
        });

        byte[]? recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(b, recvBuffer);
        Assert.Equal(sendBuffer, recvBuffer);

        await writeAndFlush;
    }

    [Fact]
    public async Task CanProperties()
    {
        (Stream s1, Stream s2) = await this.EstablishChannelStreamsAsync(string.Empty);
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
        (Stream s1, Stream s2) = await this.EstablishChannelStreamsAsync(string.Empty);
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

    [Fact]
    public async Task PartialFrameSentWithoutExplicitFlush()
    {
        (Stream s1, Stream s2) = await this.EstablishChannelStreamsAsync(string.Empty);

        byte[] smallData = new byte[] { 0x1, 0x2, 0x3 };
        await s1.WriteAsync(smallData, 0, smallData.Length).WithCancellation(this.TimeoutToken);
        byte[] recvBuffer = new byte[smallData.Length];
        await ReadAtLeastAsync(s2, new ArraySegment<byte>(recvBuffer), recvBuffer.Length, this.TimeoutToken);
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelChannelOfferBeforeAcceptance(bool cancelFirst)
    {
        var cts = new CancellationTokenSource();
        Task<MultiplexingStream.Channel>? offer = this.mx1.OfferChannelAsync(string.Empty, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => offer).WithCancellation(this.TimeoutToken);
        Stream? acceptedStream = null;
        try
        {
            // We need to test both the race condition where acceptance is sent before cancellation is received,
            // and the case where cancellation is received before we call AcceptChannelAsync.
            if (cancelFirst)
            {
                // Increase the odds that cancellation will be processed before acceptance.
                await Task.Delay(250);
            }

            MultiplexingStream.Channel? acceptedChannel = await this.mx2.AcceptChannelAsync(string.Empty, ExpectedTimeoutToken).ConfigureAwait(false);
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
        byte[]? ephemeralMessage = new byte[10];
        var random = new Random();
        random.NextBytes(ephemeralMessage);

        await Task.WhenAll(
            Task.Run(async delegate
            {
                MultiplexingStream.Channel? rpcChannel = await this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken);
                MultiplexingStream.Channel? eph = this.mx1.CreateChannel();
                await rpcChannel.Output.WriteAsync(BitConverter.GetBytes(eph.QualifiedId.Id), this.TimeoutToken);
                await eph.Output.WriteAsync(ephemeralMessage, this.TimeoutToken);
                await eph.Acceptance;
            }),
            Task.Run(async delegate
            {
                MultiplexingStream.Channel? rpcChannel = await this.mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken);
                byte[]? buffer = new byte[ephemeralMessage.Length];
                int readResult = await ReadAtLeastAsync(rpcChannel.AsStream(), new ArraySegment<byte>(buffer), sizeof(int), this.TimeoutToken);
                int channelId = BitConverter.ToInt32(buffer, 0);
                MultiplexingStream.Channel? eph = this.mx2.AcceptChannel(channelId);
                Assert.True(eph.Acceptance.IsCompleted);
                readResult = await ReadAtLeastAsync(eph.AsStream(), new ArraySegment<byte>(buffer), ephemeralMessage.Length, this.TimeoutToken);
                Assert.Equal(ephemeralMessage, buffer);
            })).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task EphemeralChannels_AcceptTwice_Throws()
    {
        await Task.WhenAll(
            Task.Run(async delegate
            {
                MultiplexingStream.Channel? rpcChannel = await this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken);
                MultiplexingStream.Channel? eph = this.mx1.CreateChannel();
                await rpcChannel.Output.WriteAsync(BitConverter.GetBytes(eph.QualifiedId.Id), this.TimeoutToken);
                await eph.Acceptance;
            }),
            Task.Run(async delegate
            {
                byte[]? buffer = new byte[sizeof(int)];
                MultiplexingStream.Channel? rpcChannel = await this.mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken);
                int readResult = await ReadAtLeastAsync(rpcChannel.AsStream(), new ArraySegment<byte>(buffer), sizeof(int), this.TimeoutToken);
                int channelId = BitConverter.ToInt32(buffer, 0);
                MultiplexingStream.Channel? eph = this.mx2.AcceptChannel(channelId);
                Assert.Throws<InvalidOperationException>(() => this.mx2.AcceptChannel(channelId));
            })).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task EphemeralChannels_Rejected()
    {
        await Task.WhenAll(
            Task.Run(async delegate
            {
                MultiplexingStream.Channel? rpcChannel = await this.mx1.OfferChannelAsync(string.Empty, this.TimeoutToken);
                MultiplexingStream.Channel? eph = this.mx1.CreateChannel();
                await rpcChannel.Output.WriteAsync(BitConverter.GetBytes(eph.QualifiedId.Id), this.TimeoutToken);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => eph.Acceptance).WithCancellation(this.TimeoutToken);
            }),
            Task.Run(async delegate
            {
                byte[]? buffer = new byte[sizeof(int)];
                MultiplexingStream.Channel? rpcChannel = await this.mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken);
                int readResult = await ReadAtLeastAsync(rpcChannel.AsStream(), new ArraySegment<byte>(buffer), sizeof(int), this.TimeoutToken);
                int channelId = BitConverter.ToInt32(buffer, 0);
                this.mx2.RejectChannel(channelId);

                // At this point, it's too late to accept
                Assert.Throws<InvalidOperationException>(() => this.mx2.AcceptChannel(channelId));
            })).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public void AcceptChannel_NeverExisted()
    {
        Assert.Throws<InvalidOperationException>(() => this.mx1.AcceptChannel(15));
    }

    [Fact]
    public async Task ChannelOfferedEvent_Anonymous_NotYetAccepted()
    {
        var mx1EventArgsSource = new TaskCompletionSource<MultiplexingStream.ChannelOfferEventArgs>();
        var mx2EventArgsSource = new TaskCompletionSource<MultiplexingStream.ChannelOfferEventArgs>();
        this.mx1.ChannelOffered += (s, e) =>
        {
            mx1EventArgsSource.SetResult(e);
        };
        this.mx2.ChannelOffered += (s, e) =>
        {
            mx2EventArgsSource.SetResult(e);
        };

        MultiplexingStream.Channel? channel = this.mx1.CreateChannel();
        MultiplexingStream.ChannelOfferEventArgs? mx2EventArgs = await mx2EventArgsSource.Task.WithCancellation(this.TimeoutToken);
        Assert.Equal(channel.QualifiedId.Id, mx2EventArgs.QualifiedId.Id);
        Assert.Equal(MultiplexingStream.ChannelSource.Remote, mx2EventArgs.QualifiedId.Source);
        Assert.Equal(string.Empty, mx2EventArgs.Name);
        Assert.False(mx2EventArgs.IsAccepted);

        Assert.False(mx1EventArgsSource.Task.IsCompleted);
    }

    [Theory]
    [PairwiseData]
    public async Task ChannelOfferedEvent_Named(bool alreadyAccepted)
    {
        var mx1EventArgsSource = new TaskCompletionSource<MultiplexingStream.ChannelOfferEventArgs>();
        var mx2EventArgsSource = new TaskCompletionSource<MultiplexingStream.ChannelOfferEventArgs>();
        this.mx1.ChannelOffered += (s, e) =>
        {
            mx1EventArgsSource.SetResult(e);
        };
        this.mx2.ChannelOffered += (s, e) =>
        {
            mx2EventArgsSource.SetResult(e);
        };

        string channelName = "abc";
        Task? acceptTask = null;
        if (alreadyAccepted)
        {
            acceptTask = this.mx2.AcceptChannelAsync(channelName, this.TimeoutToken);
        }

        Task<MultiplexingStream.Channel>? channelOfferTask = this.mx1.OfferChannelAsync(channelName, this.TimeoutToken);

        MultiplexingStream.ChannelOfferEventArgs? mx2EventArgs = await mx2EventArgsSource.Task.WithCancellation(this.TimeoutToken);
        if (!alreadyAccepted)
        {
            acceptTask = this.mx2.AcceptChannelAsync(channelName, this.TimeoutToken);
        }

        MultiplexingStream.Channel? offeredChannel = await channelOfferTask;
        Assert.Equal(offeredChannel.QualifiedId.Id, mx2EventArgs.QualifiedId.Id);
        Assert.Equal(MultiplexingStream.ChannelSource.Remote, mx2EventArgs.QualifiedId.Source);
        Assert.Equal(channelName, mx2EventArgs.Name);
        Assert.Equal(alreadyAccepted, mx2EventArgs.IsAccepted);

        Assert.False(mx1EventArgsSource.Task.IsCompleted);
        await acceptTask!.WithCancellation(this.TimeoutToken); // Rethrow any exceptions
    }

    /// <summary>
    /// Demonstrates the pattern by which one party can register for <see cref="MultiplexingStream.ChannelOffered"/> events
    /// before listening has started and thus before a race condition with an incoming offer leads to the event being raised
    /// before any handlers have been added.
    /// </summary>
    [Fact]
    public async Task ChannelOffered_AlreadyOfferedByRemote()
    {
        (this.transport1, this.transport2) = FullDuplexStream.CreatePair(new PipeOptions(pauseWriterThreshold: 2 * 1024 * 1024));
        Task<MultiplexingStream>? mx1Task = MultiplexingStream.CreateAsync(this.transport1, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion }, this.TimeoutToken);
        Task<MultiplexingStream>? mx2Task = MultiplexingStream.CreateAsync(this.transport2, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion, StartSuspended = true }, this.TimeoutToken);
        using MultiplexingStream? mx1 = await mx1Task;
        using MultiplexingStream? mx2 = await mx2Task;

        MultiplexingStream.Channel ch1 = mx1.CreateChannel();
        await Task.Delay(ExpectedTimeout); // simulate a slow receiving party
        var invoked = new AsyncManualResetEvent();
        mx2.ChannelOffered += (sender, args) => invoked.Set();
        mx2.StartListening();
        await invoked.WaitAsync(this.TimeoutToken);
    }

    [Fact]
    public void StartListening_CalledWithoutStartSuspension()
    {
        Assert.Throws<InvalidOperationException>(this.mx1.StartListening);
    }

    [Fact]
    public async Task StartListening_CalledTwice()
    {
        (this.transport1, this.transport2) = FullDuplexStream.CreatePair(new PipeOptions(pauseWriterThreshold: 2 * 1024 * 1024));
        Task<MultiplexingStream>? mx1Task = MultiplexingStream.CreateAsync(this.transport1, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion }, this.TimeoutToken);
        Task<MultiplexingStream>? mx2Task = MultiplexingStream.CreateAsync(this.transport2, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion, StartSuspended = true }, this.TimeoutToken);
        using MultiplexingStream? mx1 = await mx1Task;
        using MultiplexingStream? mx2 = await mx2Task;

        mx2.StartListening();
        Assert.Throws<InvalidOperationException>(mx2.StartListening);
    }

    [Fact]
    public async Task MessageSendingMethodsThrowBeforeListeningHasStarted()
    {
        (this.transport1, this.transport2) = FullDuplexStream.CreatePair(new PipeOptions(pauseWriterThreshold: 2 * 1024 * 1024));
        Task<MultiplexingStream>? mx1Task = MultiplexingStream.CreateAsync(this.transport1, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion }, this.TimeoutToken);
        Task<MultiplexingStream>? mx2Task = MultiplexingStream.CreateAsync(this.transport2, new MultiplexingStream.Options { ProtocolMajorVersion = this.ProtocolMajorVersion, StartSuspended = true }, this.TimeoutToken);
        using MultiplexingStream? mx1 = await mx1Task;
        using MultiplexingStream? mx2 = await mx2Task;

        await Assert.ThrowsAsync<InvalidOperationException>(() => mx2.OfferChannelAsync(string.Empty, this.TimeoutToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mx2.AcceptChannelAsync(string.Empty, this.TimeoutToken));
        Assert.Throws<InvalidOperationException>(() => mx2.CreateChannel());
        Assert.Throws<InvalidOperationException>(() => mx2.AcceptChannel(1));
        Assert.Throws<InvalidOperationException>(() => mx2.RejectChannel(1));
    }

    [Fact]
    public async Task ChannelAutoCloses_WhenBothEndsCompleteWriting()
    {
        byte[]? aMsg = new byte[] { 0x1 };
        byte[]? bMsg = new byte[] { 0x2 };
        (MultiplexingStream.Channel a, MultiplexingStream.Channel b) = await this.EstablishChannelsAsync("a");

        await a.Output.WriteAsync(aMsg, this.TimeoutToken);
        a.Output.Complete();
        ReadResult aMsgReceived = await b.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(aMsg, aMsgReceived.Buffer.First.Span.ToArray());
        b.Input.AdvanceTo(aMsgReceived.Buffer.End);
#pragma warning disable CS0618 // Type or member is obsolete
        await b.Input.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete
        Assert.True((await b.Input.ReadAsync(this.TimeoutToken)).IsCompleted);
        b.Input.Complete(); // ack the last message we received

        await b.Output.WriteAsync(bMsg, this.TimeoutToken);
        b.Output.Complete();
        ReadResult bMsgReceived = await a.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(bMsg, bMsgReceived.Buffer.First.Span.ToArray());
        a.Input.AdvanceTo(bMsgReceived.Buffer.End);
#pragma warning disable CS0618 // Type or member is obsolete
        await a.Input.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete
        Assert.True((await a.Input.ReadAsync(this.TimeoutToken)).IsCompleted);
        a.Input.Complete(); // ack the last message we received

        await Task.WhenAll(a.Completion, b.Completion).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task AcceptChannelAsync_WithExistingPipe_BeforeOffer()
    {
        var channel2OutboundPipe = new Pipe();
        var channel2InboundPipe = new Pipe();
        Stream? channel2Stream = new DuplexPipe(channel2InboundPipe.Reader, channel2OutboundPipe.Writer).AsStream();
        var channel2Options = new MultiplexingStream.ChannelOptions { ExistingPipe = new DuplexPipe(channel2OutboundPipe.Reader, channel2InboundPipe.Writer) };

        const string channelName = "channelName";
        Task<MultiplexingStream.Channel>? mx2ChannelTask = this.mx2.AcceptChannelAsync(channelName, channel2Options, this.TimeoutToken);
        Task<MultiplexingStream.Channel>? mx1ChannelTask = this.mx1.OfferChannelAsync(channelName, this.TimeoutToken);
        MultiplexingStream.Channel[]? channels = await Task.WhenAll(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);
        Stream? channel1Stream = channels[0].AsStream();

        await this.TransmitAndVerifyAsync(channel1Stream, channel2Stream, new byte[] { 1, 2, 3 });
        await this.TransmitAndVerifyAsync(channel2Stream, channel1Stream, new byte[] { 4, 5, 6 });
    }

    [Fact]
    public async Task OfferChannelAsync_WithExistingPipe()
    {
        var channel2OutboundPipe = new Pipe();
        var channel2InboundPipe = new Pipe();
        Stream? channel2Stream = new DuplexPipe(channel2InboundPipe.Reader, channel2OutboundPipe.Writer).AsStream();
        var channel2Options = new MultiplexingStream.ChannelOptions { ExistingPipe = new DuplexPipe(channel2OutboundPipe.Reader, channel2InboundPipe.Writer) };

        const string channelName = "channelName";
        Task<MultiplexingStream.Channel>? mx2ChannelTask = this.mx2.OfferChannelAsync(channelName, channel2Options, this.TimeoutToken);
        Task<MultiplexingStream.Channel>? mx1ChannelTask = this.mx1.AcceptChannelAsync(channelName, this.TimeoutToken);
        MultiplexingStream.Channel[]? channels = await Task.WhenAll(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);
        Stream? channel1Stream = channels[0].AsStream();

        await this.TransmitAndVerifyAsync(channel1Stream, channel2Stream, new byte[] { 1, 2, 3 });
        await this.TransmitAndVerifyAsync(channel2Stream, channel1Stream, new byte[] { 4, 5, 6 });
    }

    /// <summary>
    /// Create channel, send bytes before acceptance, accept and receive, then send more.
    /// </summary>
    [Fact]
    public async Task ExistingPipe_Send_Accept_Recv_Send()
    {
        byte[][]? packets = new byte[][]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 },
        };

        var channel2OutboundPipe = new Pipe();
        var channel2InboundPipe = new Pipe();
        Stream? channel2Stream = new DuplexPipe(channel2InboundPipe.Reader, channel2OutboundPipe.Writer).AsStream();
        var channel2Options = new MultiplexingStream.ChannelOptions { ExistingPipe = new DuplexPipe(channel2OutboundPipe.Reader, channel2InboundPipe.Writer) };

        // Create the channel and transmit before it is accepted.
        MultiplexingStream.Channel? channel1 = this.mx1.CreateChannel();
        Stream? channel1Stream = channel1.AsStream();
        await channel1Stream.WriteAsync(packets[0], 0, packets[0].Length, this.TimeoutToken);
        await channel1Stream.FlushAsync(this.TimeoutToken);

        // Accept the channel and read the bytes
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        MultiplexingStream.Channel? channel2 = this.mx2.AcceptChannel(channel1.QualifiedId.Id, channel2Options);
        await this.VerifyReceivedDataAsync(channel2Stream, packets[0]);

        // Verify we can transmit via the ExistingPipe.
        await this.TransmitAndVerifyAsync(channel2Stream, channel1Stream, packets[1]);

        // Verify we can receive more bytes via the ExistingPipe.
        // MANUALLY VERIFY with debugger that received bytes are read DIRECTLY into channel2InboundPipe.Writer (no intermediary buffer copying).
        await this.TransmitAndVerifyAsync(channel1Stream, channel2Stream, packets[2]);
    }

    /// <summary>
    /// Create channel, send bytes before acceptance, then more before the accepting side is done draining the Pipe.
    /// </summary>
    [Fact]
    public async Task ExistingPipe_Send_Accept_Send()
    {
        byte[][]? packets = new byte[][]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
        };

        var emptyReaderPipe = new Pipe();
        emptyReaderPipe.Writer.Complete();
        var slowWriter = new SlowPipeWriter();
        var channel2Options = new MultiplexingStream.ChannelOptions
        {
            ExistingPipe = new DuplexPipe(emptyReaderPipe.Reader, slowWriter),
        };

        // Create the channel and transmit before it is accepted.
        MultiplexingStream.Channel? channel1 = this.mx1.CreateChannel();
        Stream? channel1Stream = channel1.AsStream();
        await channel1Stream.WriteAsync(packets[0], 0, packets[0].Length, this.TimeoutToken);
        await channel1Stream.FlushAsync(this.TimeoutToken);

        // Accept the channel
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        MultiplexingStream.Channel? channel2 = this.mx2.AcceptChannel(channel1.QualifiedId.Id, channel2Options);

        // Send MORE bytes
        await channel1Stream.WriteAsync(packets[1], 0, packets[1].Length, this.TimeoutToken);
        await channel1Stream.FlushAsync(this.TimeoutToken);

        // Allow the copying of the first packet to our ExistingPipe to complete.
        slowWriter.UnblockFlushAsync.Set();

        // Wait for all bytes to be transmitted
        channel1.Output.Complete();
        await slowWriter.Completion;

        // Verify that we received them all, in order.
        Assert.Equal(packets[0].Concat(packets[1]).ToArray(), slowWriter.WrittenBytes.ToArray());
    }

    [Fact]
    public async Task CreateChannel_BlastLotsOfData()
    {
        const int DataSize = 1024 * 1024;
        var channelOptions = new MultiplexingStream.ChannelOptions
        {
            ChannelReceivingWindowSize = DataSize,
        };

        MultiplexingStream.Channel? channel1 = this.mx1.CreateChannel(channelOptions);

        // Blast a bunch of data to the channel as soon as it is accepted.
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        MultiplexingStream.Channel? channel2 = this.mx2.AcceptChannel(channel1.QualifiedId.Id, channelOptions);
        await channel2.Output.WriteAsync(new byte[DataSize], this.TimeoutToken);

        // Read all the data, such that the PipeReader has to buffer until the whole payload is read.
        // Since this is a LOT of data, this would normally overrun the Pipe's max buffer size.
        // If this succeeds, then we know the PipeOptions instance we supplied was taken into account.
        int bytesRead = 0;
        while (bytesRead < DataSize)
        {
            ReadResult readResult = await channel1.Input.ReadAsync(this.TimeoutToken);
            bytesRead += (int)readResult.Buffer.Length;
            SequencePosition consumed = bytesRead == DataSize ? readResult.Buffer.End : readResult.Buffer.Start;
            channel1.Input.AdvanceTo(consumed, readResult.Buffer.End);
        }
    }

    [Theory]
    [PairwiseData]
    public async Task AcceptChannel_InputPipeOptions(bool acceptBeforeTransmit)
    {
        // We have to use a smaller data size when transmitting before acceptance to avoid a deadlock due to the limited buffer size of channels.
        int dataSize = acceptBeforeTransmit ? 1024 * 1024 : 16 * 1024;

        var channelOptions = new MultiplexingStream.ChannelOptions
        {
            ChannelReceivingWindowSize = 2 * 1024 * 1024,
        };

        MultiplexingStream.Channel? channel1 = this.mx1.CreateChannel();
        await this.WaitForEphemeralChannelOfferToPropagateAsync();
        var bytesWrittenEvent = new AsyncManualResetEvent();

        await Task.WhenAll(Party1Async(), Party2Async());

        async Task Party1Async()
        {
            if (acceptBeforeTransmit)
            {
                await channel1.Acceptance.WithCancellation(this.TimeoutToken);
            }

            await channel1.Output.WriteAsync(new byte[dataSize], this.TimeoutToken);
            bytesWrittenEvent.Set();
        }

        async Task Party2Async()
        {
            if (!acceptBeforeTransmit)
            {
                await bytesWrittenEvent.WaitAsync(this.TimeoutToken);
            }

            MultiplexingStream.Channel? channel2 = this.mx2.AcceptChannel(channel1.QualifiedId.Id, channelOptions);

            // Read all the data, such that the PipeReader has to buffer until the whole payload is read.
            // Since this is a LOT of data, this would normally overrun the Pipe's max buffer size.
            // If this succeeds, then we know the PipeOptions instance we supplied was taken into account.
            int bytesRead = 0;
            while (bytesRead < dataSize)
            {
                ReadResult readResult = await channel2.Input.ReadAsync(this.TimeoutToken);
                bytesRead += (int)readResult.Buffer.Length;
                SequencePosition consumed = bytesRead == dataSize ? readResult.Buffer.End : readResult.Buffer.Start;
                channel2.Input.AdvanceTo(consumed, readResult.Buffer.End);
            }
        }
    }

    [Fact]
    public virtual async Task SeededChannels()
    {
        (Stream, Stream) pair = FullDuplexStream.CreatePair();
        var options = new MultiplexingStream.Options
        {
            ProtocolMajorVersion = this.ProtocolMajorVersion,
            SeededChannels =
            {
               new MultiplexingStream.ChannelOptions { },
               new MultiplexingStream.ChannelOptions { },
            },
        };
        await Assert.ThrowsAsync<NotSupportedException>(() => Task.WhenAll(
            MultiplexingStream.CreateAsync(pair.Item1, options, this.TimeoutToken),
            MultiplexingStream.CreateAsync(pair.Item2, options, this.TimeoutToken)));
    }

    /// <summary>
    /// Verifies that faulting a <see cref="PipeReader"/> that receives channel data will not adversely impact other channels on the <see cref="MultiplexingStream"/>.
    /// </summary>
    [Fact]
    public async Task FaultingChannelReader()
    {
        Task<MultiplexingStream.Channel> baselineOffer = this.mx1.OfferChannelAsync("baseline", cancellationToken: this.TimeoutToken);
        Task<MultiplexingStream.Channel> sketchyOffer = this.mx1.OfferChannelAsync("sketchy", cancellationToken: this.TimeoutToken);

        MultiplexingStream.Channel mx2Baseline = await this.mx2.AcceptChannelAsync("baseline", this.TimeoutToken);
        MultiplexingStream.Channel mx2Sketchy = await this.mx2.AcceptChannelAsync("sketchy", this.TimeoutToken);

        MultiplexingStream.Channel mx1Baseline = await baselineOffer;
        MultiplexingStream.Channel mx1Sketchy = await sketchyOffer;

        // Now fault one reader
        await mx2Sketchy.Input.CompleteAsync(new InvalidOperationException("Sketchy reader fail."));

        // Transmit data on this channel from the other side to force the mxstream to notice that we faulted a reader.
        await mx1Sketchy.Output.WriteAsync(new byte[3], this.TimeoutToken);

        // Verify communication over the good channel.
        await mx1Baseline.Output.WriteAsync(new byte[3], this.TimeoutToken);
        await this.ReadAtLeastAsync(mx2Baseline.Input, 3);
    }

    protected static Task CompleteChannelsAsync(params MultiplexingStream.Channel[] channels)
    {
        foreach (MultiplexingStream.Channel? channel in channels)
        {
            channel.Output.Complete();
        }

        return Task.WhenAll(channels.Select(c => c.Completion));
    }

    protected async Task WaitForEphemeralChannelOfferToPropagateAsync()
    {
        // Propagation of ephemeral channel offers must occur before the remote end can accept it.
        // The simplest way to guarantee that the offer has propagated is to send another message after the offer
        // and wait for that message to be received.
        // The "message" we send is an offer for a named channel, since that can be awaited on to accept.
        const string ChannelName = "EphemeralChannelWaiter";
        MultiplexingStream.Channel[]? channels = await Task.WhenAll(
            this.mx1.OfferChannelAsync(ChannelName, this.TimeoutToken),
            this.mx2.AcceptChannelAsync(ChannelName, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
        channels[0].Dispose();
        channels[1].Dispose();
    }

    protected async Task<(MultiplexingStream.Channel, MultiplexingStream.Channel)> EstablishChannelsAsync(string identifier, long? receivingWindowSize = null)
    {
        var channelOptions = new MultiplexingStream.ChannelOptions { ChannelReceivingWindowSize = receivingWindowSize };
        Task<MultiplexingStream.Channel>? mx1ChannelTask = this.mx1.OfferChannelAsync(identifier, channelOptions, this.TimeoutToken);
        Task<MultiplexingStream.Channel>? mx2ChannelTask = this.mx2.AcceptChannelAsync(identifier, channelOptions, this.TimeoutToken);
        MultiplexingStream.Channel[]? channels = await WhenAllSucceedOrAnyFail(mx1ChannelTask, mx2ChannelTask).WithCancellation(this.TimeoutToken);
        Assert.NotNull(channels[0]);
        Assert.NotNull(channels[1]);
        return (channels[0], channels[1]);
    }

    protected async Task<(Stream, Stream)> EstablishChannelStreamsAsync(string identifier, long? receivingWindowSize = null)
    {
        (MultiplexingStream.Channel channel1, MultiplexingStream.Channel channel2) = await this.EstablishChannelsAsync(identifier, receivingWindowSize);
        return (channel1.AsStream(), channel2.AsStream());
    }

    protected class SlowPipeWriter : PipeWriter
    {
        private readonly Sequence<byte> writtenBytes = new Sequence<byte>();

        private readonly TaskCompletionSource<object?> completionSource = new TaskCompletionSource<object?>();

        private CancellationTokenSource nextFlushToken = new CancellationTokenSource();

        internal AsyncManualResetEvent UnblockFlushAsync { get; } = new AsyncManualResetEvent();

        internal ReadOnlySequence<byte> WrittenBytes => this.writtenBytes.AsReadOnlySequence;

        internal Task Completion => this.completionSource.Task;

        public override void Advance(int bytes)
        {
            Verify.Operation(!this.completionSource.Task.IsCompleted, "Writing already completed.");
            this.writtenBytes.Advance(bytes);
        }

        public override void CancelPendingFlush() => this.nextFlushToken.Cancel();

        public override void Complete(Exception? exception = null)
        {
            if (exception == null)
            {
                this.completionSource.TrySetResult(null);
            }
            else
            {
                this.completionSource.TrySetException(exception);
            }
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await this.UnblockFlushAsync.WaitAsync(cancellationToken);
                return default;
            }
            finally
            {
                if (this.nextFlushToken.IsCancellationRequested)
                {
                    this.nextFlushToken = new CancellationTokenSource();
                }
            }
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            Verify.Operation(!this.completionSource.Task.IsCompleted, "Writing already completed.");
            return this.writtenBytes.GetMemory(sizeHint);
        }

        public override Span<byte> GetSpan(int sizeHint = 0) => this.GetMemory(sizeHint).Span;

        [Obsolete]
        public override void OnReaderCompleted(Action<Exception?, object?> callback, object? state)
        {
            // We don't have a reader that consumers of this mock need to worry about,
            // so just say we're done when the writing is done.
            this.Completion.ContinueWith(c => callback(c.Exception, state), TaskScheduler.Default).Forget();
        }
    }
}
