// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Nerdbank.Streams;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Xunit.Abstractions;

[Obsolete("Tests functionality that .NET now exposes directly through PipeWriter.Create(Stream)")]
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
        Stream unreadableStream = Substitute.For<Stream>();
        unreadableStream.CanWrite.Returns(true);

        // Set up for either WriteAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        unreadableStream.WriteAsync(default, CancellationToken.None).ThrowsForAnyArgs(expectedException);
#else
        unreadableStream.WriteAsync(null, 0, 0, CancellationToken.None).ThrowsAsyncForAnyArgs(expectedException);
#endif

        PipeWriter? writer = this.CreatePipeWriter(unreadableStream);
        InvalidOperationException? actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(new byte[1], this.TimeoutToken).AsTask());
        Assert.Same(expectedException, actualException);
    }

    [Fact]
    public async Task CancelPendingFlush_WithToken()
    {
        Stream streamMock = Substitute.For<Stream>();
        streamMock.CanWrite.Returns(true);
        var writeCompletedSource = new TaskCompletionSource<object?>();

        // Set up for either WriteAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        streamMock.WriteAsync(null, CancellationToken.None).ReturnsForAnyArgs(new ValueTask(writeCompletedSource.Task));
#else
        streamMock.WriteAsync(null, 0, 0, CancellationToken.None).ReturnsForAnyArgs(writeCompletedSource.Task);
#endif

        PipeWriter? writer = this.CreatePipeWriter(streamMock);
        writer.GetMemory(1);
        writer.Advance(1);
        var cts = new CancellationTokenSource();
        ValueTask<FlushResult> flushTask = writer.FlushAsync(cts.Token);
        cts.Cancel();
        writeCompletedSource.SetResult(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flushTask.AsTask());
    }

    [Fact]
    public async Task CancelPendingFlush()
    {
        Stream streamMock = Substitute.For<Stream>();
        streamMock.CanWrite.Returns(true);
        var writeCompletedSource = new TaskCompletionSource<object?>();

        // Set up for either WriteAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        streamMock.WriteAsync(null, CancellationToken.None).ReturnsForAnyArgs(new ValueTask(writeCompletedSource.Task));
#else
        streamMock.WriteAsync(null, 0, 0, CancellationToken.None).ReturnsForAnyArgs(writeCompletedSource.Task);
#endif

        PipeWriter? writer = this.CreatePipeWriter(streamMock);
        writer.GetMemory(1);
        writer.Advance(1);
        ValueTask<FlushResult> flushTask = writer.FlushAsync();
        writer.CancelPendingFlush();
        writeCompletedSource.SetResult(null);
        FlushResult flushResult = await flushTask;
        Assert.True(flushResult.IsCanceled);
    }

    [Fact]
    public async Task Write_MultipleBlocks()
    {
        var ms = new MemoryStream();
        PipeWriter? w = ms.UseStrictPipeWriter();
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
        PipeWriter? writer = this.CreatePipeWriter(stream);
        writer.GetMemory(1);
        writer.Advance(1);
        writer.CancelPendingFlush();
        FlushResult flushResult = await writer.FlushAsync(this.TimeoutToken);
        Assert.False(flushResult.IsCanceled);
        Assert.Equal(1, stream.Length);
    }

    protected override PipeWriter CreatePipeWriter(Stream stream) => stream.UseStrictPipeWriter();
}
