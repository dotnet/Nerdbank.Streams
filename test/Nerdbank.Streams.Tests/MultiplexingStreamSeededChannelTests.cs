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

public class MultiplexingStreamSeededChannelTests : TestBase, IAsyncLifetime
{
    private Stream transport1;
    private Stream transport2;
    private MultiplexingStream mx1;
    private MultiplexingStream mx2;
    private MultiplexingStream.Options options;

    public MultiplexingStreamSeededChannelTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.options = new MultiplexingStream.Options
        {
            ProtocolMajorVersion = 3,
            SeededChannels =
            {
               new MultiplexingStream.ChannelOptions { },
               new MultiplexingStream.ChannelOptions { },
               new MultiplexingStream.ChannelOptions { },
            },
        };

        var mx1TraceSource = new TraceSource(nameof(this.mx1), SourceLevels.All);
        var mx2TraceSource = new TraceSource(nameof(this.mx2), SourceLevels.All);

        mx1TraceSource.Listeners.Add(new XunitTraceListener(this.Logger));
        mx2TraceSource.Listeners.Add(new XunitTraceListener(this.Logger));

        Func<string, MultiplexingStream.QualifiedChannelId, string, TraceSource> traceSourceFactory = (string mxInstanceName, MultiplexingStream.QualifiedChannelId id, string name) =>
        {
            var traceSource = new TraceSource(mxInstanceName + " channel " + id, SourceLevels.All);
            traceSource.Listeners.Clear(); // remove DefaultTraceListener
            traceSource.Listeners.Add(new XunitTraceListener(this.Logger));
            return traceSource;
        };

        Func<MultiplexingStream.QualifiedChannelId, string, TraceSource> mx1TraceSourceFactory = (MultiplexingStream.QualifiedChannelId id, string name) => traceSourceFactory(nameof(this.mx1), id, name);
        Func<MultiplexingStream.QualifiedChannelId, string, TraceSource> mx2TraceSourceFactory = (MultiplexingStream.QualifiedChannelId id, string name) => traceSourceFactory(nameof(this.mx2), id, name);

        (this.transport1, this.transport2) = FullDuplexStream.CreatePair(new PipeOptions(pauseWriterThreshold: 2 * 1024 * 1024));
        this.mx1 = MultiplexingStream.Create(this.transport1, new MultiplexingStream.Options(this.options) { TraceSource = mx1TraceSource, DefaultChannelTraceSourceFactoryWithQualifier = mx1TraceSourceFactory });
        this.mx2 = MultiplexingStream.Create(this.transport2, new MultiplexingStream.Options(this.options) { TraceSource = mx2TraceSource, DefaultChannelTraceSourceFactoryWithQualifier = mx2TraceSourceFactory });
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
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

    [Fact]
    public async Task SeededChannels_SendContent()
    {
        var channel1_0 = this.mx1.AcceptChannel(0);
        var channel1_1 = this.mx1.AcceptChannel(1);
        var channel2_0 = this.mx2.AcceptChannel(0);
        var channel2_1 = this.mx2.AcceptChannel(1);

        await this.TransmitAndVerifyAsync(channel1_0.AsStream(), channel2_0.AsStream(), new byte[] { 1, 2, 3 });
        await this.TransmitAndVerifyAsync(channel1_1.AsStream(), channel2_1.AsStream(), new byte[] { 4, 5, 6 });
    }

    [Fact]
    public async Task SeededChannels_CanBeClosed()
    {
        var channel1 = this.mx1.AcceptChannel(0);
        channel1.Output.Complete();
        channel1.Input.Complete();

        var channel2 = this.mx2.AcceptChannel(0);
        var readResult = await channel2.Input.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);
        channel2.Output.Complete();

        await channel1.Completion.WithCancellation(this.TimeoutToken);
        await channel2.Completion.WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public void SeededChannels_CannotBeRejected()
    {
        Assert.Throws<InvalidOperationException>(() => this.mx1.RejectChannel(0));
    }

    [Fact]
    public void SeededChannels_CannotBeAcceptedTwice()
    {
        this.mx1.AcceptChannel(0);
        Assert.Throws<InvalidOperationException>(() => this.mx1.AcceptChannel(0));
    }

    [Fact]
    public void CreateChannel_DoesNotOverlapSeededChannelIDs()
    {
        var channel = this.mx1.CreateChannel();
        Assert.True(channel.QualifiedId.Id >= (ulong)this.options.SeededChannels.Count);
    }

    [Fact]
    public void Create_VersionsWithHandshakes()
    {
        var pair = FullDuplexStream.CreatePair();
        Assert.Throws<NotSupportedException>(() => MultiplexingStream.Create(pair.Item1, new MultiplexingStream.Options { ProtocolMajorVersion = 1 }));
        Assert.Throws<NotSupportedException>(() => MultiplexingStream.Create(pair.Item1, new MultiplexingStream.Options { ProtocolMajorVersion = 2 }));
    }
}
