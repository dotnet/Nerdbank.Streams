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

public class StreamExtensionsTests : TestBase
{
    public StreamExtensionsTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void UsePipeReader_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.UsePipeReader((Stream)null));
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.UsePipeReader((WebSocket)null));
    }

    [Fact]
    public void UsePipeReader_NonReadableStream()
    {
        var unreadableStream = new Mock<Stream>(MockBehavior.Strict);
        unreadableStream.SetupGet(s => s.CanRead).Returns(false);
        Assert.Throws<ArgumentException>(() => unreadableStream.Object.UsePipeReader());
        unreadableStream.VerifyAll();
    }

    [Fact]
    public async Task UsePipeReader_Stream()
    {
        byte[] expectedBuffer = GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer);
        var reader = stream.UsePipeReader(sizeHint: 50);

        Task writerCompletedTask = reader.WaitForWriterCompletionAsync();
        Assert.False(writerCompletedTask.IsCompleted);

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
    public async Task UsePipeReader_Stream_ReadAsyncAfterExamining()
    {
        byte[] expectedBuffer = GetRandomBuffer(2048);
        var stream = new HalfDuplexStream();
        stream.Write(expectedBuffer, 0, 50);
        var reader = stream.UsePipeReader(sizeHint: 50);
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
        var result2 = await resultTask2;
        Assert.True(result2.Buffer.Length > result.Buffer.Length);

        // Now consume everything and get even more.
        reader.AdvanceTo(result2.Buffer.End);
        stream.Write(expectedBuffer, 100, expectedBuffer.Length - 100);
        ReadResult result3 = await reader.ReadAsync(this.TimeoutToken);
        Assert.True(result3.Buffer.Length > 0);
    }

    [Fact]
    public async Task UsePipeReader_Stream_TryRead()
    {
        byte[] expectedBuffer = GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer);
        var reader = stream.UsePipeReader(sizeHint: 50);
        byte[] actualBuffer = new byte[expectedBuffer.Length];

        ReadResult result = await reader.ReadAsync(this.TimeoutToken);
        reader.AdvanceTo(result.Buffer.GetPosition(1), result.Buffer.GetPosition(2)); // do not "examine" all the bytes so that TryRead will find it.

        Assert.True(reader.TryRead(out result));
        Assert.False(result.IsCanceled);
        Assert.Equal(expectedBuffer.AsSpan(1, 20).ToArray(), result.Buffer.First.Span.Slice(0, 20).ToArray());

        reader.AdvanceTo(result.Buffer.End);
    }

    [Fact]
    public async Task UsePipeReader_StreamFails()
    {
        var expectedException = new InvalidOperationException();
        var unreadableStream = new Mock<Stream>(MockBehavior.Strict);
        unreadableStream.SetupGet(s => s.CanRead).Returns(true);

        // Set up for either ReadAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if NETCOREAPP2_1
        unreadableStream.Setup(s => s.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>())).ThrowsAsync(expectedException);
#else
        unreadableStream.Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(expectedException);
#endif

        var reader = unreadableStream.Object.UsePipeReader();
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken));
        Assert.Same(expectedException, actualException);
    }

    [Fact]
    public async Task UsePipeReader_Stream_OnWriterCompleted()
    {
        byte[] expectedBuffer = GetRandomBuffer(50);
        var stream = new MemoryStream(expectedBuffer);
        var reader = stream.UsePipeReader(sizeHint: 50);
        byte[] actualBuffer = new byte[expectedBuffer.Length];

        // The exception throwing test is disabled due to https://github.com/dotnet/corefx/issues/31695
        ////// This will verify that a callback that throws doesn't stop subsequent callbacks from being invoked.
        ////reader.OnWriterCompleted((ex, s) => throw new InvalidOperationException(), null);
        Task writerCompletedTask = reader.WaitForWriterCompletionAsync();

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
        await reader.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
    }

    [Fact(Skip = "Bizarre behavior when using the built-in Pipe class: https://github.com/dotnet/corefx/issues/31696")]
    public async Task UsePipeReader_Stream_CancelPendingRead()
    {
        var stream = new HalfDuplexStream();
        var reader = stream.UsePipeReader(sizeHint: 50);

        ValueTask<ReadResult> readTask = reader.ReadAsync(this.TimeoutToken);
        reader.CancelPendingRead();
        var readResult = await readTask.AsTask().WithCancellation(this.TimeoutToken);
        Assert.True(readResult.IsCanceled);
        ////reader.AdvanceTo(readResult.Buffer.End);

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

    [Fact]
    public async Task UsePipeReader_Stream_CancelPendingRead_WithCancellationToken()
    {
        var stream = new HalfDuplexStream();
        var reader = stream.UsePipeReader(sizeHint: 50);

        var cts = new CancellationTokenSource();
        ValueTask<ReadResult> readTask = reader.ReadAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask.AsTask());
    }

    [Fact]
    public void UsePipeWriter_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.UsePipeWriter((Stream)null));
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.UsePipeWriter((WebSocket)null));
    }

    [Fact]
    public void UsePipeWriter_NonReadableStream()
    {
        var unreadableStream = new Mock<Stream>(MockBehavior.Strict);
        unreadableStream.SetupGet(s => s.CanWrite).Returns(false);
        Assert.Throws<ArgumentException>(() => unreadableStream.Object.UsePipeWriter());
        unreadableStream.VerifyAll();
    }

    [Fact]
    public async Task UsePipeWriter_Stream()
    {
        byte[] expectedBuffer = GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer.Length);
        var writer = stream.UsePipeWriter(this.TimeoutToken);
        await writer.WriteAsync(expectedBuffer.AsMemory(0, 1024), this.TimeoutToken);
        await writer.WriteAsync(expectedBuffer.AsMemory(1024, 1024), this.TimeoutToken);

        // As a means of waiting for the async process that copies what we write onto the stream,
        // complete our writer and wait for the reader to complete also.
        writer.Complete();
        await writer.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken);

        Assert.Equal(expectedBuffer, stream.ToArray());
    }

    [Fact]
    public async Task UsePipeWriter_StreamFails()
    {
        var expectedException = new InvalidOperationException();
        var unreadableStream = new Mock<Stream>(MockBehavior.Strict);
        unreadableStream.SetupGet(s => s.CanWrite).Returns(true);

        // Set up for either ReadAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if NETCOREAPP2_1
        unreadableStream.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Throws(expectedException);
#else
        unreadableStream.Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(expectedException);
#endif

        var writer = unreadableStream.Object.UsePipeWriter();
        await writer.WriteAsync(new byte[1], this.TimeoutToken);
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken));
        Assert.Same(expectedException, actualException);
    }

    [Fact]
    public async Task UsePipeWriter_Stream_TryWriteAfterComplete()
    {
        byte[] expectedBuffer = GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer.Length);
        var writer = stream.UsePipeWriter();
        await writer.WriteAsync(expectedBuffer, this.TimeoutToken);
        writer.Complete();
        Assert.Throws<InvalidOperationException>(() => writer.GetMemory());
        Assert.Throws<InvalidOperationException>(() => writer.GetSpan());
        Assert.Throws<InvalidOperationException>(() => writer.Advance(0));
    }

    [Fact]
    public async Task UsePipeWriter_Stream_Flush_Precanceled()
    {
        byte[] expectedBuffer = GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer.Length);
        var writer = stream.UsePipeWriter();
        var memory = writer.GetMemory(expectedBuffer.Length);
        expectedBuffer.CopyTo(memory);
        writer.Advance(expectedBuffer.Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.FlushAsync(new CancellationToken(true)).AsTask());
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
        var pipeWriter = webSocket.UsePipeWriter(this.TimeoutToken);
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

    private static byte[] GetRandomBuffer(int length)
    {
        var buffer = new byte[length];
        var random = new Random();
        random.NextBytes(buffer);
        return buffer;
    }
}
