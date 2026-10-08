// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

[InheritsTests]
public class StreamUsePipeWriterTests : StreamPipeWriterTestBase
{
    public StreamUsePipeWriterTests()
        : base(TestOutputHelper.Instance)
    {
    }

    [Test]
    public async Task StreamFails()
    {
        var expectedException = new InvalidOperationException();
        Stream unreadableStream = Substitute.For<Stream>();
        unreadableStream.CanWrite.Returns(true);

        // Set up for either WriteAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        unreadableStream.WriteAsync(null, CancellationToken.None).ThrowsForAnyArgs(expectedException);
#else
        unreadableStream.WriteAsync(null, 0, 0, CancellationToken.None).ThrowsAsyncForAnyArgs(expectedException);
#endif

        PipeWriter? writer = this.CreatePipeWriter(unreadableStream);
        await writer.WriteAsync(new byte[1], this.TimeoutToken);
#pragma warning disable CS0618 // Type or member is obsolete
        InvalidOperationException? actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken));
#pragma warning restore CS0618 // Type or member is obsolete
        Assert.Same(expectedException, actualException);
    }

#if NETFRAMEWORK
    [Test]
    public async Task PipeStreamUsesApmDirectly()
    {
        using var stream = new ApmTrackingPipeStream();
        PipeWriter writer = this.CreatePipeWriter(stream);

        await writer.WriteAsync(new byte[] { 1 }, this.TimeoutToken);
        while (stream.BeginWriteCallCount < 1)
        {
            await Task.Delay(10, this.TimeoutToken);
        }

        await writer.WriteAsync(new byte[] { 2 }, this.TimeoutToken);
        writer.Complete();
#pragma warning disable CS0618 // Type or member is obsolete
        await writer.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Equal(new byte[] { 1, 2 }, stream.ToArray());
        Assert.Equal(0, stream.WriteAsyncCallCount);
        Assert.True(stream.BeginWriteCallCount > 0);
    }
#endif

    protected override PipeWriter CreatePipeWriter(Stream stream) => stream.UsePipeWriter();
}
