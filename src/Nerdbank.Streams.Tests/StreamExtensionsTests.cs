// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.IO.Pipelines;
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
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.UsePipeReader(null));
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
    public async Task UsePipeReader()
    {
        byte[] expectedBuffer = GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer);
        var reader = stream.UsePipeReader(readBufferSize: 50, this.TimeoutToken);
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

        // Verify that the end of the stream caused the reader to complete.
        var lastResult = await reader.ReadAsync(this.TimeoutToken);
        Assert.Equal(0, lastResult.Buffer.Length);
        Assert.True(lastResult.IsCompleted);

        // Verify we got the right content.
        Assert.Equal(expectedBuffer, actualBuffer);
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
    public void UsePipeWriter_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.UsePipeWriter(null));
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
    public async Task UsePipeWriter()
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

    private static byte[] GetRandomBuffer(int length)
    {
        var buffer = new byte[length];
        var random = new Random();
        random.NextBytes(buffer);
        return buffer;
    }
}
