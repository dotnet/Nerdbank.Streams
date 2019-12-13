// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Moq;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public abstract class StreamPipeReaderTestBase : TestBase
{
    public StreamPipeReaderTestBase(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => this.CreatePipeReader((Stream)null!));
    }

    [Fact]
    public void NonReadableStream()
    {
        var unreadableStream = new Mock<Stream>(MockBehavior.Strict);
        unreadableStream.SetupGet(s => s.CanRead).Returns(false);
        Assert.Throws<ArgumentException>(() => this.CreatePipeReader(unreadableStream.Object));
        unreadableStream.VerifyAll();
    }

    [Fact]
    public async Task Stream()
    {
        byte[] expectedBuffer = this.GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer);
        var reader = this.CreatePipeReader(stream, sizeHint: 50);

#pragma warning disable CS0618 // Type or member is obsolete
        Task writerCompletedTask = reader.WaitForWriterCompletionAsync();
#pragma warning restore CS0618 // Type or member is obsolete

        // This next assertion is only guaranteeed for the strict pipe reader.
        // For the normal one, its role of reading from the stream is concurrent with our code,
        // and thus may have already completed.
        if (this is StreamUseStrictPipeReaderTests)
        {
            Assert.False(writerCompletedTask.IsCompleted);
        }

        byte[] actualBuffer = new byte[expectedBuffer.Length];
        int bytesRead = 0;
        while (bytesRead < expectedBuffer.Length)
        {
            var result = await reader.ReadAsync(this.TimeoutToken);
            foreach (ReadOnlyMemory<byte> segment in result.Buffer)
            {
                segment.CopyTo(new Memory<byte>(actualBuffer, bytesRead, actualBuffer.Length - bytesRead));
                bytesRead += segment.Length;
            }

            reader.AdvanceTo(result.Buffer.End);
        }

        // Verify that the end of the stream causes the reader to report completion.
        var lastResult = await reader.ReadAsync(this.TimeoutToken);
        Assert.Equal(0, lastResult.Buffer.Length);
        Assert.True(lastResult.IsCompleted);

        // Verify that the writer is completed
        await writerCompletedTask.WithCancellation(this.TimeoutToken);

        // Complete the reader and verify subsequent behavior.
        reader.Complete();
        Assert.Throws<InvalidOperationException>(() => reader.TryRead(out lastResult));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => reader.ReadAsync(this.TimeoutToken).AsTask());

        // Verify we got the right content.
        Assert.Equal(expectedBuffer, actualBuffer);
    }

    [Fact]
    public async Task ReadAsyncAfterExamining()
    {
        byte[] expectedBuffer = this.GetRandomBuffer(2048);
        var stream = new HalfDuplexStream();
        stream.Write(expectedBuffer, 0, 50);
        await stream.FlushAsync(this.TimeoutToken);
        var reader = this.CreatePipeReader(stream, sizeHint: 50);
        byte[] actualBuffer = new byte[expectedBuffer.Length];

        ReadResult result = await reader.ReadAsync(this.TimeoutToken);
        reader.AdvanceTo(result.Buffer.Start, result.Buffer.GetPosition(1));

        // Since we didn't examine all the bytes already in the buffer, the next read should be synchronous,
        // and shouldn't give us any more buffer.
        ValueTask<ReadResult> resultTask = reader.ReadAsync(this.TimeoutToken);
        Assert.True(resultTask.IsCompleted);
        Assert.Equal(result.Buffer.Length, resultTask.Result.Buffer.Length);

        // Now examine everything, but don't consume it. We should get more.
        reader.AdvanceTo(resultTask.Result.Buffer.Start, resultTask.Result.Buffer.End);
        ValueTask<ReadResult> resultTask2 = reader.ReadAsync(this.TimeoutToken);
        Assert.False(resultTask2.IsCompleted);
        stream.Write(expectedBuffer, 50, 50);
        await stream.FlushAsync(this.TimeoutToken);
        var result2 = await resultTask2;
        Assert.True(result2.Buffer.Length > result.Buffer.Length);

        // Now consume everything and get even more.
        reader.AdvanceTo(result2.Buffer.End);
        stream.Write(expectedBuffer, 100, expectedBuffer.Length - 100);
        await stream.FlushAsync(this.TimeoutToken);
        ReadResult result3 = await reader.ReadAsync(this.TimeoutToken);
        Assert.True(result3.Buffer.Length > 0);
    }

    [Fact]
    public async Task TryRead()
    {
        byte[] expectedBuffer = this.GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer);
        var reader = this.CreatePipeReader(stream, sizeHint: 50);
        byte[] actualBuffer = new byte[expectedBuffer.Length];

        ReadResult result = await reader.ReadAsync(this.TimeoutToken);
        reader.AdvanceTo(result.Buffer.GetPosition(1), result.Buffer.GetPosition(2)); // do not "examine" all the bytes so that TryRead will find it.

        Assert.True(reader.TryRead(out result));
        Assert.False(result.IsCanceled);
        Assert.Equal(expectedBuffer.AsSpan(1, 20).ToArray(), result.Buffer.First.Span.Slice(0, 20).ToArray());

        reader.AdvanceTo(result.Buffer.End);
    }

    [Theory, PairwiseData]
    public async Task ReadAsync_TwiceInARow(bool emptyStream)
    {
        var stream = new MemoryStream(emptyStream ? Array.Empty<byte>() : new byte[3]);
        var reader = this.CreatePipeReader(stream, sizeHint: 50);
        await reader.ReadAsync(this.TimeoutToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync(this.TimeoutToken));
    }

    [Theory, PairwiseData]
    public async Task TryRead_TwiceInARow(bool emptyStream)
    {
        var stream = new MemoryStream(emptyStream ? Array.Empty<byte>() : new byte[3]);
        var reader = this.CreatePipeReader(stream, sizeHint: 50);

        // Read asynchronously once to guarantee that there's a non-empty buffer in the reader.
        var readResult = await reader.ReadAsync(this.TimeoutToken);
        reader.AdvanceTo(readResult.Buffer.Start);

        Assert.True(reader.TryRead(out _));
        Assert.Throws<InvalidOperationException>(() => reader.TryRead(out _));
    }

    [Fact]
    public void AdvanceTo_BeforeRead()
    {
        var stream = new MemoryStream();
        var reader = this.CreatePipeReader(stream, sizeHint: 50);
        Assert.Throws<InvalidOperationException>(() => reader.AdvanceTo(default));
        var ex = Assert.ThrowsAny<Exception>(() => reader.AdvanceTo(ReadOnlySequence<byte>.Empty.Start));
        Assert.True(ex is InvalidCastException || ex is InvalidOperationException);
    }

    [Fact]
    public async Task OnWriterCompleted()
    {
        byte[] expectedBuffer = this.GetRandomBuffer(50);
        var stream = new MemoryStream(expectedBuffer);
        var reader = this.CreatePipeReader(stream, sizeHint: 50);
        byte[] actualBuffer = new byte[expectedBuffer.Length];

        // The exception throwing test is disabled due to https://github.com/dotnet/corefx/issues/31695
        ////// This will verify that a callback that throws doesn't stop subsequent callbacks from being invoked.
        ////reader.OnWriterCompleted((ex, s) => throw new InvalidOperationException(), null);
#pragma warning disable CS0618 // Type or member is obsolete
        Task writerCompletedTask = reader.WaitForWriterCompletionAsync();
#pragma warning restore CS0618 // Type or member is obsolete

        // Read everything.
        while (true)
        {
            var result = await reader.ReadAsync(this.TimeoutToken).AsTask().WithCancellation(this.TimeoutToken);
            reader.AdvanceTo(result.Buffer.End);
            if (result.IsCompleted)
            {
                break;
            }
        }

        // Verify that our second callback fires:
        await writerCompletedTask.WithCancellation(this.TimeoutToken);

        // Verify that a callback only registered now gets invoked too:
#pragma warning disable CS0618 // Type or member is obsolete
        await reader.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    [Fact]
    public async Task CancelPendingRead_WithCancellationToken()
    {
        var stream = new HalfDuplexStream();
        var reader = this.CreatePipeReader(stream, sizeHint: 50);

        var cts = new CancellationTokenSource();
        ValueTask<ReadResult> readTask = reader.ReadAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask.AsTask());
    }

    [Fact]
    public async Task Complete_DoesNotCauseStreamDisposal()
    {
        var stream = new HalfDuplexStream();
        var reader = this.CreatePipeReader(stream);
        reader.Complete();

        var timeout = ExpectedTimeoutToken;
        while (!stream.IsDisposed && !timeout.IsCancellationRequested)
        {
            await Task.Yield();
        }

        Assert.False(stream.IsDisposed);
    }

    protected abstract PipeReader CreatePipeReader(Stream stream, int sizeHint = 0);
}
