// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class StreamUseStrictPipeWriterTests : StreamPipeWriterTestBase
{
    public StreamUseStrictPipeWriterTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public async Task StreamFails()
    {
        var expectedException = new InvalidOperationException();
        var unreadableStream = new Mock<Stream>(MockBehavior.Strict);
        unreadableStream.SetupGet(s => s.CanWrite).Returns(true);

        // Set up for either WriteAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        unreadableStream.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Throws(expectedException);
#else
        unreadableStream.Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(expectedException);
#endif

        var writer = this.CreatePipeWriter(unreadableStream.Object);
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(new byte[1], this.TimeoutToken).AsTask());
        Assert.Same(expectedException, actualException);
    }

    [Fact]
    public async Task CancelPendingFlush_WithToken()
    {
        var streamMock = new Mock<Stream>(MockBehavior.Strict);
        streamMock.SetupGet(s => s.CanWrite).Returns(true);
        var writeCompletedSource = new TaskCompletionSource<object>();

        // Set up for either WriteAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        streamMock.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Returns(new ValueTask(writeCompletedSource.Task));
#else
        streamMock.Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(writeCompletedSource.Task);
#endif

        var stream = streamMock.Object;
        var writer = this.CreatePipeWriter(stream);
        writer.GetMemory(1);
        writer.Advance(1);
        var cts = new CancellationTokenSource();
        var flushTask = writer.FlushAsync(cts.Token);
        cts.Cancel();
        writeCompletedSource.SetResult(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flushTask.AsTask());
    }

    [Fact]
    public async Task CancelPendingFlush()
    {
        var streamMock = new Mock<Stream>(MockBehavior.Strict);
        streamMock.SetupGet(s => s.CanWrite).Returns(true);
        var writeCompletedSource = new TaskCompletionSource<object>();

        // Set up for either WriteAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        streamMock.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Returns(new ValueTask(writeCompletedSource.Task));
#else
        streamMock.Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(writeCompletedSource.Task);
#endif

        var stream = streamMock.Object;
        var writer = this.CreatePipeWriter(stream);
        writer.GetMemory(1);
        writer.Advance(1);
        var flushTask = writer.FlushAsync();
        writer.CancelPendingFlush();
        writeCompletedSource.SetResult(null);
        var flushResult = await flushTask;
        Assert.True(flushResult.IsCanceled);
    }

    [Fact]
    public async Task Write_MultipleBlocks()
    {
        var ms = new MemoryStream();
        var w = ms.UseStrictPipeWriter();
        w.GetMemory(4);
        w.Advance(4);
        w.GetMemory(1024);
        w.Advance(1024);
        await w.FlushAsync(this.TimeoutToken);
        Assert.Equal(1028, ms.Length);
    }

    [Fact] // Not applied to base test class because of https://github.com/dotnet/corefx/issues/31837
    public async Task CancelPendingFlush_BeforeFlushDoesNotCancelFutureFlush()
    {
        var stream = new MemoryStream();
        var writer = this.CreatePipeWriter(stream);
        writer.GetMemory(1);
        writer.Advance(1);
        writer.CancelPendingFlush();
        var flushResult = await writer.FlushAsync(this.TimeoutToken);
        Assert.False(flushResult.IsCanceled);
        Assert.Equal(1, stream.Length);
    }

    protected override PipeWriter CreatePipeWriter(Stream stream) => stream.UseStrictPipeWriter();
}
