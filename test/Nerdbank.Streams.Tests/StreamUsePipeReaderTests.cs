// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

[InheritsTests]
public class StreamUsePipeReaderTests : StreamPipeReaderTestBase
{
    public StreamUsePipeReaderTests()
        : base(TestOutputHelper.Instance)
    {
    }

    protected override bool EmulatePipelinesStreamPipeReader => false;

    [Test]
    public async Task StreamFails()
    {
        var expectedException = new InvalidOperationException();
        Stream unreadableStream = Substitute.For<Stream>();
        unreadableStream.CanRead.Returns(true);

        // Set up for either ReadAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        unreadableStream.ReadAsync(default, CancellationToken.None).ThrowsAsyncForAnyArgs(expectedException);
#else
        unreadableStream.ReadAsync(null, 0, 0, CancellationToken.None).ThrowsAsyncForAnyArgs(expectedException);
#endif

        PipeReader? reader = this.CreatePipeReader(unreadableStream);
#pragma warning disable CS0618 // Type or member is obsolete
        InvalidOperationException? actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken));
#pragma warning restore CS0618 // Type or member is obsolete
        Assert.Same(expectedException, actualException);
    }

    [Test]
    public async Task Complete_CausesWriterCompletion()
    {
        var stream = new SimplexStream();
        PipeReader? reader = this.CreatePipeReader(stream);
#pragma warning disable CS0618 // Type or member is obsolete
        Task writerCompletion = reader.WaitForWriterCompletionAsync();
#pragma warning restore CS0618 // Type or member is obsolete
        reader.Complete();
        await writerCompletion.WithCancellation(this.TimeoutToken);
    }

    [Test]
    public void NonReadableStream()
    {
        Stream unreadableStream = Substitute.For<Stream>();
        Assert.Throws<ArgumentException>(() => this.CreatePipeReader(unreadableStream));
        _ = unreadableStream.Received().CanRead;
    }

#if NETFRAMEWORK
    [Test]
    public async Task PipeStreamUsesApmDirectly()
    {
        using var stream = new ApmTrackingPipeStream(new byte[] { 1 });
        PipeReader reader = this.CreatePipeReader(stream, hintSize: 1);

        ReadResult result = await reader.ReadAsync(this.TimeoutToken);
        Assert.Equal(1, result.Buffer.Length);
        reader.AdvanceTo(result.Buffer.End);

        result = await reader.ReadAsync(this.TimeoutToken);
        Assert.True(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.End);
        reader.Complete();

        Assert.Equal(0, stream.ReadAsyncCallCount);
        Assert.True(stream.BeginReadCallCount > 0);
    }
#endif

    protected override PipeReader CreatePipeReader(Stream stream, int hintSize = 0) => stream.UsePipeReader(hintSize);
}
