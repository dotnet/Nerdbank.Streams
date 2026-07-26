// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

    [Fact]
    public async Task ReaderCompletionUnblocksRemoteWriter()
    {
        MultiplexingStream.Channel a = await this.mx1.OfferChannelAsync("test", this.TimeoutToken);
        MultiplexingStream.Channel b = await this.mx2.AcceptChannelAsync("test", this.TimeoutToken);

        a.Output.Write(new byte[this.mx1.DefaultChannelReceivingWindowSize].AsSpan());
        ValueTask<FlushResult> flush = a.Output.FlushAsync(this.TimeoutToken);
        await b.Input.CompleteAsync();

        Assert.True((await flush.WithCancellation(this.TimeoutToken)).IsCompleted);
    }
}
