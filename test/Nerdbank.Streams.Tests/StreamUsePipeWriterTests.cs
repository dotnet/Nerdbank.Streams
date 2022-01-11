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

public class StreamUsePipeWriterTests : StreamPipeWriterTestBase
{
    public StreamUsePipeWriterTests(ITestOutputHelper logger)
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

        PipeWriter? writer = this.CreatePipeWriter(unreadableStream.Object);
        await writer.WriteAsync(new byte[1], this.TimeoutToken);
#pragma warning disable CS0618 // Type or member is obsolete
        InvalidOperationException? actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken));
#pragma warning restore CS0618 // Type or member is obsolete
        Assert.Same(expectedException, actualException);
    }

    protected override PipeWriter CreatePipeWriter(Stream stream) => stream.UsePipeWriter();
}
