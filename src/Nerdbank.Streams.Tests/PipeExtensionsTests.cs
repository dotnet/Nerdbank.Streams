// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using Moq;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class PipeExtensionsTests : TestBase
{
    public PipeExtensionsTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void UsePipeReader_WebSocket_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => PipeExtensions.UsePipeReader((WebSocket)null));
    }

    [Fact]
    public void UsePipeWriter_WebSocket_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => PipeExtensions.UsePipeWriter((WebSocket)null));
    }

    [Fact]
    public async Task UsePipe_Stream()
    {
        var ms = new HalfDuplexStream();
        IDuplexPipe pipe = ms.UsePipe(cancellationToken: this.TimeoutToken);
        await pipe.Output.WriteAsync(new byte[] { 1, 2, 3 }, this.TimeoutToken);
        var readResult = await pipe.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(3, readResult.Buffer.Length);
        pipe.Input.AdvanceTo(readResult.Buffer.End);
    }

    [Fact]
    public async Task UsePipe_Stream_Disposal()
    {
        var ms = new HalfDuplexStream();
        IDuplexPipe pipe = ms.UsePipe(cancellationToken: this.TimeoutToken);
        pipe.Output.Complete();
        pipe.Input.Complete();
        while (!ms.IsDisposed && !this.TimeoutToken.IsCancellationRequested)
        {
            await Task.Yield();
        }

        Assert.True(ms.IsDisposed);
    }

    [Theory]
    [PairwiseData]
    public async Task UsePipe_Stream_OneDirectionDoesNotDispose(bool completeOutput)
    {
        var ms = new HalfDuplexStream();
        IDuplexPipe pipe = ms.UsePipe(cancellationToken: this.TimeoutToken);
        if (completeOutput)
        {
            pipe.Output.Complete();
        }
        else
        {
            pipe.Input.Complete();
        }

        var timeout = ExpectedTimeoutToken;
        while (!ms.IsDisposed && !timeout.IsCancellationRequested)
        {
            await Task.Yield();
        }

        Assert.False(ms.IsDisposed);
    }

    [Fact]
    public async Task UsePipeReader_WebSocket()
    {
        var expectedBuffer = new byte[] { 4, 5, 6 };
        var webSocket = new MockWebSocket();
        webSocket.EnqueueRead(expectedBuffer);
        var pipeReader = webSocket.UsePipeReader(cancellationToken: this.TimeoutToken);
        var readResult = await pipeReader.ReadAsync(this.TimeoutToken);
        Assert.Equal(expectedBuffer, readResult.Buffer.First.Span.ToArray());
        pipeReader.AdvanceTo(readResult.Buffer.End);
    }

    [Fact]
    public async Task UsePipeWriter_WebSocket()
    {
        var expectedBuffer = new byte[] { 4, 5, 6 };
        var webSocket = new MockWebSocket();
        var pipeWriter = webSocket.UsePipeWriter(cancellationToken: this.TimeoutToken);
        await pipeWriter.WriteAsync(expectedBuffer, this.TimeoutToken);
        pipeWriter.Complete();
        await pipeWriter.WaitForReaderCompletionAsync();
        var message = webSocket.WrittenQueue.Dequeue();
        Assert.Equal(expectedBuffer, message.Buffer.ToArray());
    }

    [Fact]
    public async Task UsePipe_WebSocket()
    {
        var expectedBuffer = new byte[] { 4, 5, 6 };
        var webSocket = new MockWebSocket();
        webSocket.EnqueueRead(expectedBuffer);
        var pipe = webSocket.UsePipe(cancellationToken: this.TimeoutToken);

        var readResult = await pipe.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(expectedBuffer, readResult.Buffer.First.Span.ToArray());
        pipe.Input.AdvanceTo(readResult.Buffer.End);

        await pipe.Output.WriteAsync(expectedBuffer, this.TimeoutToken);
        pipe.Output.Complete();
        await pipe.Output.WaitForReaderCompletionAsync();
        var message = webSocket.WrittenQueue.Dequeue();
        Assert.Equal(expectedBuffer, message.Buffer.ToArray());
    }
}
