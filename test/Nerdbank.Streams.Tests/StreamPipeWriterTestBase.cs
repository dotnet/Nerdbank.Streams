// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using Xunit;
using Xunit.Abstractions;

public abstract class StreamPipeWriterTestBase : TestBase
{
    protected StreamPipeWriterTestBase(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => this.CreatePipeWriter(null!));
    }

    [Fact]
    public void NonReadableStream()
    {
        Stream unreadableStream = Substitute.For<Stream>();
        Assert.Throws<ArgumentException>(() => this.CreatePipeWriter(unreadableStream));
        _ = unreadableStream.Received().CanWrite;
    }

    [Fact]
    public async Task Stream()
    {
        byte[] expectedBuffer = this.GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer.Length);
        PipeWriter? writer = this.CreatePipeWriter(stream);
        await writer.WriteAsync(expectedBuffer.AsMemory(0, 1024), this.TimeoutToken);
        await writer.WriteAsync(expectedBuffer.AsMemory(1024, 1024), this.TimeoutToken);

        // As a means of waiting for the async process that copies what we write onto the stream,
        // complete our writer and wait for the reader to complete also.
        writer.Complete();
#pragma warning disable CS0618 // Type or member is obsolete
        await writer.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Equal(expectedBuffer, stream.ToArray());
    }

    [Fact]
    public async Task TryWriteAfterComplete()
    {
        byte[] expectedBuffer = this.GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer.Length);
        PipeWriter? writer = this.CreatePipeWriter(stream);
        await writer.WriteAsync(expectedBuffer, this.TimeoutToken);
        writer.Complete();
        Assert.Throws<InvalidOperationException>(() => writer.GetMemory());
        Assert.Throws<InvalidOperationException>(() => writer.GetSpan());
        writer.Advance(0);
    }

    [Fact]
    public async Task Flush_Precanceled()
    {
        byte[] expectedBuffer = this.GetRandomBuffer(2048);
        var stream = new MemoryStream(expectedBuffer.Length);
        PipeWriter? writer = this.CreatePipeWriter(stream);
        Memory<byte> memory = writer.GetMemory(expectedBuffer.Length);
        expectedBuffer.CopyTo(memory);
        writer.Advance(expectedBuffer.Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.FlushAsync(new CancellationToken(true)).AsTask());
    }

    [Fact]
    public async Task OnReaderCompleted()
    {
        var stream = new MemoryStream();
        PipeWriter? writer = this.CreatePipeWriter(stream);
#pragma warning disable CS0618 // Type or member is obsolete
        Task readerCompleted = writer.WaitForReaderCompletionAsync();
#pragma warning restore CS0618 // Type or member is obsolete
        writer.Complete();
        await readerCompleted.WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task Complete_WithUnflushedWrittenBytes()
    {
        var stream = new MemoryStream();
        PipeWriter? writer = this.CreatePipeWriter(stream);
#pragma warning disable CS0618 // Type or member is obsolete
        Task readerCompleted = writer.WaitForReaderCompletionAsync();
#pragma warning restore CS0618 // Type or member is obsolete
        Memory<byte> mem = writer.GetMemory(1);
        writer.Advance(1);

        // Calling Complete implicitly causes the reader to have access to all unflushed buffers.
        Assert.Equal(0, stream.Length);
        writer.Complete();
        await readerCompleted.WithCancellation(this.TimeoutToken);
        Assert.Equal(1, stream.Length);
    }

    protected abstract PipeWriter CreatePipeWriter(Stream stream);
}
