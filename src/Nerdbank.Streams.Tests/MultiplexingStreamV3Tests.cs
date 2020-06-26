// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
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
        var pair = FullDuplexStream.CreatePair();
        var mx1 = MultiplexingStream.Create(pair.Item1);
        var mx2 = MultiplexingStream.Create(pair.Item2);

        await Task.WhenAll(mx1.OfferChannelAsync("test"), mx2.AcceptChannelAsync("test"));
        await mx1.DisposeAsync();
        await mx2.DisposeAsync();
    }

    [Fact]
    public void Create_VersionsWithHandshakes()
    {
        var pair = FullDuplexStream.CreatePair();
        Assert.Throws<NotSupportedException>(() => MultiplexingStream.Create(pair.Item1, new MultiplexingStream.Options { ProtocolMajorVersion = 1 }));
        Assert.Throws<NotSupportedException>(() => MultiplexingStream.Create(pair.Item1, new MultiplexingStream.Options { ProtocolMajorVersion = 2 }));
    }
}
