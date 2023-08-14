// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.IO.Pipelines;
using Nerdbank.Streams;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Xunit.Abstractions;

[Obsolete("Tests functionality that .NET now exposes directly through PipeReader.Create(Stream)")]
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
        Stream unreadableStream = Substitute.For<Stream>();
        unreadableStream.CanRead.Returns(true);

        // Set up for either ReadAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        unreadableStream.ReadAsync(default, CancellationToken.None).ThrowsAsyncForAnyArgs(expectedException);
#else
        unreadableStream.ReadAsync(null, 0, 0, CancellationToken.None).ThrowsAsyncForAnyArgs(expectedException);
#endif

        PipeReader? reader = this.CreatePipeReader(unreadableStream);
        InvalidOperationException? actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(this.TimeoutToken).AsTask());
        Assert.Same(expectedException, actualException);
    }

    [Fact]
    public void Read()
    {
        MemoryStream ms = new(new byte[] { 1, 2, 3 });
        var reader = (StreamPipeReader)this.CreatePipeReader(ms);
        ReadResult result = reader.Read();
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Buffer.ToArray());
        Assert.False(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.GetPosition(1));
        result = reader.Read();
        Assert.Equal(new byte[] { 2, 3 }, result.Buffer.ToArray());
        Assert.False(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.End);
        result = reader.Read();
        Assert.Equal(0, result.Buffer.Length);
        Assert.True(result.IsCompleted);
    }

    protected override PipeReader CreatePipeReader(Stream stream, int sizeHint = 0) => stream.UseStrictPipeReader(sizeHint);
}
