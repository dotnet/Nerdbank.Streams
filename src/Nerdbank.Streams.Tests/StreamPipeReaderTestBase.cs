// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

    protected virtual bool EmulatePipelinesStreamPipeReader => true;

    [Fact]
    public void ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => this.CreatePipeReader((Stream)null!));
    }

    [SkippableFact]
    public async Task Stream()
    {
        Skip.If(this is IOPipelinesStreamPipeReaderTests, "OnWriterCompleted isn't supported.");

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
        var stream = new SimplexStream();
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

    [Fact]
    public void TryRead_FalseStillTurnsOnReadingMode()
    {
        var reader = this.CreatePipeReader(new MemoryStream(new byte[] { 1, 2, 3 }));

        bool tryReadResult = reader.TryRead(out var readResult);

        // The non-strict PipeReader is timing sensitive and may or may not be able to synchronously produce a read.
        // So only assert the False result for the strict readers.
        if (this.EmulatePipelinesStreamPipeReader)
        {
            Assert.False(tryReadResult);
        }

        reader.AdvanceTo(readResult.Buffer.End);
    }

    [Fact]
    public void TryRead_FalseCanBeCalledRepeatedly()
    {
        var reader = this.CreatePipeReader(new MemoryStream(new byte[] { 1, 2, 3 }));

        // Verify that it's safe to call TryRead repeatedly when it returns False.
        Assert.False(reader.TryRead(out var readResult));
        Assert.False(reader.TryRead(out readResult));
    }

    [Fact]
    public async Task TryRead_AdvanceTo_AfterEndReached()
    {
        var reader = this.CreatePipeReader(new MemoryStream(new byte[] { 1, 2, 3 }));

        var readResult = await reader.ReadAsync(this.TimeoutToken);
        reader.AdvanceTo(readResult.Buffer.End);

        reader.TryRead(out readResult);
        reader.AdvanceTo(readResult.Buffer.End);
    }

    [Theory, PairwiseData]
    public async Task ReadAsync_TwiceInARow(bool emptyStream)
    {
        var stream = new MemoryStream(emptyStream ? Array.Empty<byte>() : new byte[3]);
        var reader = this.CreatePipeReader(stream, sizeHint: 50);
        var result1 = await reader.ReadAsync(this.TimeoutToken);
        if (emptyStream)
        {
            Assert.True(result1.IsCompleted);
        }

        if (this.EmulatePipelinesStreamPipeReader)
        {
            var result2 = await reader.ReadAsync(this.TimeoutToken);
            Assert.Equal(result1.Buffer.Length, result2.Buffer.Length);
            Assert.Equal(emptyStream, result2.IsCompleted);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync(this.TimeoutToken));
        }
    }

    [Theory, PairwiseData]
    public async Task TryRead_TwiceInARow(bool emptyStream)
    {
        var stream = new MemoryStream(emptyStream ? Array.Empty<byte>() : new byte[3]);
        var reader = this.CreatePipeReader(stream, sizeHint: 50);

        // Read asynchronously once to guarantee that there's a non-empty buffer in the reader.
        var readResult = await reader.ReadAsync(this.TimeoutToken);
        reader.AdvanceTo(readResult.Buffer.Start);

        Assert.Equal(!emptyStream || !this.EmulatePipelinesStreamPipeReader, reader.TryRead(out _));
        if (this.EmulatePipelinesStreamPipeReader)
        {
            Assert.Equal(!emptyStream, reader.TryRead(out _));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => reader.TryRead(out _));
        }
    }

    [Fact]
    public void AdvanceTo_BeforeRead()
    {
        var stream = new MemoryStream();
        var reader = this.CreatePipeReader(stream, sizeHint: 50);
        if (this.EmulatePipelinesStreamPipeReader)
        {
            reader.AdvanceTo(default);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => reader.AdvanceTo(default));
        }

        var ex = Assert.ThrowsAny<Exception>(() => reader.AdvanceTo(ReadOnlySequence<byte>.Empty.Start));
        Assert.True(ex is InvalidCastException || ex is InvalidOperationException);
    }

    [SkippableFact]
    public async Task OnWriterCompleted()
    {
        Skip.If(this is IOPipelinesStreamPipeReaderTests, "OnWriterCompleted isn't supported.");
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
        var stream = new SimplexStream();
        var reader = this.CreatePipeReader(stream, sizeHint: 50);

        var cts = new CancellationTokenSource();
        ValueTask<ReadResult> readTask = reader.ReadAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask.AsTask());
    }

    [Fact]
    public async Task Complete_MayCauseStreamDisposal()
    {
        var stream = new SimplexStream();
        var ms = new MonitoringStream(stream);
        var disposal = new AsyncManualResetEvent();
        ms.Disposed += (s, e) => disposal.Set();
        var reader = this.CreatePipeReader(ms);
        reader.Complete();

        if (this is IOPipelinesStreamPipeReaderTests)
        {
            await disposal.WaitAsync(this.TimeoutToken);
        }
        else
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => disposal.WaitAsync(ExpectedTimeoutToken));
        }
    }

    [Fact]
    public async Task CancelPendingRead()
    {
        var stream = new SimplexStream();
        var reader = this.CreatePipeReader(stream, sizeHint: 50);

        ValueTask<ReadResult> readTask = reader.ReadAsync(this.TimeoutToken);
        reader.CancelPendingRead();
        var readResult = await readTask.AsTask().WithCancellation(this.TimeoutToken);
        Assert.True(readResult.IsCanceled);

        // Verify we can read after that without cancellation.
        readTask = reader.ReadAsync(this.TimeoutToken);
        stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
        await stream.FlushAsync(this.TimeoutToken);
        readResult = await readTask;
        Assert.False(readResult.IsCanceled);
        Assert.Equal(3, readResult.Buffer.Length);
        reader.AdvanceTo(readResult.Buffer.End);

        // Now cancel again
        readTask = reader.ReadAsync(this.TimeoutToken);
        reader.CancelPendingRead();
        readResult = await readTask;
        Assert.True(readResult.IsCanceled);
    }

    protected abstract PipeReader CreatePipeReader(Stream stream, int sizeHint = 0);
}
