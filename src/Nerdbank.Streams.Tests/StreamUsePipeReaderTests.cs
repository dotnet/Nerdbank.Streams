// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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

public class StreamUsePipeReaderTests : StreamPipeReaderTestBase
{
    public StreamUsePipeReaderTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public async Task StreamFails()
    {
        var expectedException = new InvalidOperationException();
        var unreadableStream = new Mock<Stream>(MockBehavior.Strict);
        unreadableStream.SetupGet(s => s.CanRead).Returns(true);

        // Set up for either ReadAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        unreadableStream.Setup(s => s.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>())).ThrowsAsync(expectedException);
#else
        unreadableStream.Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(expectedException);
#endif

        var reader = this.CreatePipeReader(unreadableStream.Object);
#pragma warning disable CS0618 // Type or member is obsolete
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken));
#pragma warning restore CS0618 // Type or member is obsolete
        Assert.Same(expectedException, actualException);
    }

    [Fact]
    public async Task Complete_CausesWriterCompletion()
    {
        var stream = new SimplexStream();
        var reader = this.CreatePipeReader(stream);
#pragma warning disable CS0618 // Type or member is obsolete
        Task writerCompletion = reader.WaitForWriterCompletionAsync();
#pragma warning restore CS0618 // Type or member is obsolete
        reader.Complete();
        await writerCompletion.WithCancellation(this.TimeoutToken);
    }

    protected override PipeReader CreatePipeReader(Stream stream, int hintSize = 0) => stream.UsePipeReader(hintSize);
}
