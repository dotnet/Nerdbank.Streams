// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class MultiplexingStreamV3Tests : MultiplexingStreamV2Tests
{
    public MultiplexingStreamV3Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override int ProtocolMajorVersion => 3;

    /// <summary>
    /// Verify the <see cref="MultiplexingStream.Create(System.IO.Stream, MultiplexingStream.Options?)"/> method,
    /// since all the inherited tests run based on the <see cref="MultiplexingStream.CreateAsync(System.IO.Stream, MultiplexingStream.Options?, System.Threading.CancellationToken)"/> method.
    /// </summary>
    [Fact]
    public async Task Create()
    {
        (Stream, Stream) pair = FullDuplexStream.CreatePair();
        var mx1 = MultiplexingStream.Create(pair.Item1);
        var mx2 = MultiplexingStream.Create(pair.Item2);

        await Task.WhenAll(mx1.OfferChannelAsync("test"), mx2.AcceptChannelAsync("test"));
        await mx1.DisposeAsync();
        await mx2.DisposeAsync();
    }

    [Fact]
    public override async Task SeededChannels()
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
        var mx1 = MultiplexingStream.Create(pair.Item1, options);
        var mx2 = MultiplexingStream.Create(pair.Item2, options);

        MultiplexingStream.Channel? channel1_0 = mx1.AcceptChannel(0);
        MultiplexingStream.Channel? channel1_1 = mx1.AcceptChannel(1);
        MultiplexingStream.Channel? channel2_0 = mx2.AcceptChannel(0);
        MultiplexingStream.Channel? channel2_1 = mx2.AcceptChannel(1);

        await this.TransmitAndVerifyAsync(channel1_0.AsStream(), channel2_0.AsStream(), new byte[] { 1, 2, 3 });
        await this.TransmitAndVerifyAsync(channel1_1.AsStream(), channel2_1.AsStream(), new byte[] { 4, 5, 6 });
    }
}
