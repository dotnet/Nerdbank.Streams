// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class StreamExtensionsTests : TestBase
{
    private readonly Stream dataStream;

    private byte[] buffer = new byte[10];

    public StreamExtensionsTests(ITestOutputHelper logger)
        : base(logger)
    {
        var seq = new Sequence<byte>();
        seq.Write(new byte[] { 1, 2 });
        seq.Write(new byte[] { 3, 4 });
        seq.Write(new byte[] { 5 });
        this.dataStream = new ChunkyReadStream(seq);
    }

    [Fact]
    public async Task ReadBlockAsync_AmpleBytes()
    {
        int bytesRead = await this.dataStream.ReadBlockAsync(this.buffer.AsMemory(0, 3));
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3 }, this.buffer.AsMemory(0, 3).ToArray());
    }

    [Fact]
    public async Task ReadBlockAsync_ExactByteCount()
    {
        int bytesRead = await this.dataStream.ReadBlockAsync(this.buffer.AsMemory(0, 5));
        Assert.Equal(5, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, this.buffer.AsMemory(0, 5).ToArray());
    }

    [Fact]
    public async Task ReadBlockAsync_InsufficientBytes()
    {
        int bytesRead = await this.dataStream.ReadBlockAsync(this.buffer.AsMemory(0, 6));
        Assert.Equal(5, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, this.buffer.AsMemory(0, 5).ToArray());
    }

    [Fact]
    public async Task ReadBlockOrThrowAsync_AmpleBytes()
    {
        await this.dataStream.ReadBlockOrThrowAsync(this.buffer.AsMemory(0, 3));
        Assert.Equal(new byte[] { 1, 2, 3 }, this.buffer.AsMemory(0, 3).ToArray());
    }

    [Fact]
    public async Task ReadBlockOrThrowAsync_ExactByteCount()
    {
        await this.dataStream.ReadBlockOrThrowAsync(this.buffer.AsMemory(0, 5));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, this.buffer.AsMemory(0, 5).ToArray());
    }

    [Fact]
    public async Task ReadBlockOrThrowAsync_InsufficientBytes()
    {
        await Assert.ThrowsAsync<EndOfStreamException>(() => this.dataStream.ReadBlockOrThrowAsync(this.buffer.AsMemory(0, 6)).AsTask());
    }

    private class ChunkyReadStream : Stream
    {
        private ReadOnlySequence<byte> chunks;

        internal ChunkyReadStream(ReadOnlySequence<byte> chunks)
        {
            this.chunks = chunks;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => this.chunks.Length;

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.chunks.IsEmpty)
            {
                return 0;
            }

            int bytesToRead = Math.Min(count, this.chunks.First.Length);
            this.chunks.First.Span.Slice(0, bytesToRead).CopyTo(buffer.AsSpan(offset, bytesToRead));
            this.chunks = this.chunks.Slice(bytesToRead);
            return bytesToRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
