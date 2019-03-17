// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

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

public class StreamUseStrictPipeReaderTests : StreamPipeReaderTestBase
{
    public StreamUseStrictPipeReaderTests(ITestOutputHelper logger)
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
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(this.TimeoutToken).AsTask());
        Assert.Same(expectedException, actualException);
    }

    [Fact] // Bizarre behavior when using the built-in Pipe class: https://github.com/dotnet/corefx/issues/31696
    public async Task CancelPendingRead()
    {
        var stream = new HalfDuplexStream();
        var reader = this.CreatePipeReader(stream, sizeHint: 50);

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

    protected override PipeReader CreatePipeReader(Stream stream, int sizeHint = 0) => stream.UseStrictPipeReader(sizeHint);
}
