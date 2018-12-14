// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class ReadOnlySequenceStreamTests : TestBase
{
    private static readonly ReadOnlySequence<byte> DefaultSequence = default;

    private static readonly ReadOnlySequence<byte> SimpleSequence = new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 });

    private static readonly ReadOnlySequence<byte> MultiBlockSequence;

    private readonly Stream defaultStream;

    static ReadOnlySequenceStreamTests()
    {
        var seg3 = new SeqSegment(new byte[] { 7, 8, 9 }, null);
        var seg2 = new SeqSegment(new byte[] { 4, 5, 6 }, seg3);
        var seg1 = new SeqSegment(new byte[] { 1, 2, 3 }, seg2);
        MultiBlockSequence = new ReadOnlySequence<byte>(seg1, 0, seg3, seg3.Memory.Length);
    }

    public ReadOnlySequenceStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.defaultStream = DefaultSequence.AsStream();
    }

    [Fact]
    public void Read_EmptySequence()
    {
        Assert.Equal(0, this.defaultStream.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void Length()
    {
        Assert.Equal(0, this.defaultStream.Length);
        this.defaultStream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.defaultStream.Length);
    }

    [Fact]
    public void SetLength()
    {
        Assert.Throws<NotSupportedException>(() => this.defaultStream.SetLength(0));
        this.defaultStream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.defaultStream.SetLength(0));
    }

    [Fact]
    public void CanSeek()
    {
        Assert.True(this.defaultStream.CanSeek);
        this.defaultStream.Dispose();
        Assert.False(this.defaultStream.CanSeek);
    }

    [Fact]
    public void CanRead()
    {
        Assert.True(this.defaultStream.CanRead);
        this.defaultStream.Dispose();
        Assert.False(this.defaultStream.CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.False(this.defaultStream.CanWrite);
        this.defaultStream.Dispose();
        Assert.False(this.defaultStream.CanWrite);
    }

    [Fact]
    public void CanTimeout()
    {
        Assert.False(this.defaultStream.CanTimeout);
        this.defaultStream.Dispose();
        Assert.False(this.defaultStream.CanTimeout);
    }

    [Fact]
    public void Position()
    {
        Assert.Equal(0, this.defaultStream.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => this.defaultStream.Position = 1);

        var simpleStream = SimpleSequence.AsStream();
        Assert.Equal(0, simpleStream.Position);
        simpleStream.Position++;
        Assert.Equal(1, simpleStream.Position);

        var multiBlockStream = MultiBlockSequence.AsStream();
        Assert.Equal(0, multiBlockStream.Position = 0);
        Assert.Equal(multiBlockStream.Position + 1, multiBlockStream.ReadByte());

        Assert.Equal(4, multiBlockStream.Position = 4);
        Assert.Equal(multiBlockStream.Position + 1, multiBlockStream.ReadByte());

        Assert.Equal(5, multiBlockStream.Position = 5);
        Assert.Equal(multiBlockStream.Position + 1, multiBlockStream.ReadByte());

        Assert.Equal(0, multiBlockStream.Position = 0);
        Assert.Equal(multiBlockStream.Position + 1, multiBlockStream.ReadByte());

        Assert.Equal(9, multiBlockStream.Position = 9);
        Assert.Equal(-1, multiBlockStream.ReadByte());

        Assert.Throws<ArgumentOutOfRangeException>(() => multiBlockStream.Position = 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => multiBlockStream.Position = -1);
    }

    [Fact]
    public void IsDisposed()
    {
        Assert.False(((IDisposableObservable)this.defaultStream).IsDisposed);
        this.defaultStream.Dispose();
        Assert.True(((IDisposableObservable)this.defaultStream).IsDisposed);
    }

    [Fact]
    public void Flush()
    {
        Assert.Throws<NotSupportedException>(() => this.defaultStream.Flush());
        this.defaultStream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.defaultStream.Flush());
    }

    [Fact]
    public async Task FlushAsync()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => this.defaultStream.FlushAsync());
        this.defaultStream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.defaultStream.FlushAsync());
    }

    [Fact]
    public void Write()
    {
        Assert.Throws<NotSupportedException>(() => this.defaultStream.Write(new byte[1], 0, 1));
#if NETCOREAPP2_1
        Assert.Throws<NotSupportedException>(() => this.defaultStream.Write(new byte[1].AsSpan(0, 1)));
#endif
        this.defaultStream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.defaultStream.Write(new byte[1], 0, 1));
#if NETCOREAPP2_1
        Assert.Throws<ObjectDisposedException>(() => this.defaultStream.Write(new byte[1].AsSpan(0, 1)));
#endif
    }

    [Fact]
    public async Task WriteAsync()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => this.defaultStream.WriteAsync(new byte[1], 0, 1));
#if NETCOREAPP2_1
        await Assert.ThrowsAsync<NotSupportedException>(() => this.defaultStream.WriteAsync(new byte[1].AsMemory(0, 1)).AsTask());
#endif
        this.defaultStream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.defaultStream.WriteAsync(new byte[1], 0, 1));
#if NETCOREAPP2_1
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.defaultStream.WriteAsync(new byte[1].AsMemory(0, 1)).AsTask());
#endif
    }

    [Fact]
    public void WriteByte()
    {
        Assert.Throws<NotSupportedException>(() => this.defaultStream.WriteByte(1));
        this.defaultStream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.defaultStream.WriteByte(1));
    }

    [Fact]
    public void Seek_EmptyStream()
    {
        var stream = DefaultSequence.AsStream();
        Assert.Equal(0, stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(0, stream.Seek(0, SeekOrigin.Current));
        Assert.Equal(0, stream.Seek(0, SeekOrigin.End));
    }

    [Fact]
    public void Seek()
    {
        var stream = MultiBlockSequence.AsStream();
        Assert.Equal(0, stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(0, stream.Position);
        Assert.Equal(stream.Position + 1, stream.ReadByte());

        Assert.Equal(4, stream.Seek(4, SeekOrigin.Begin));
        Assert.Equal(4, stream.Position);
        Assert.Equal(stream.Position + 1, stream.ReadByte());

        Assert.Equal(7, stream.Seek(7, SeekOrigin.Begin));
        Assert.Equal(7, stream.Position);
        Assert.Equal(stream.Position + 1, stream.ReadByte());

        Assert.Equal(9, stream.Seek(1, SeekOrigin.Current));
        Assert.Equal(9, stream.Position);

        Assert.Equal(1, stream.Seek(-8, SeekOrigin.Current));
        Assert.Equal(1, stream.Position);
        Assert.Equal(stream.Position + 1, stream.ReadByte());

        Assert.Equal(5, stream.Seek(3, SeekOrigin.Current));
        Assert.Equal(5, stream.Position);
        Assert.Equal(stream.Position + 1, stream.ReadByte());

        Assert.Equal(9, stream.Seek(0, SeekOrigin.End));
        Assert.Equal(9, stream.Position);
        Assert.Equal(-1, stream.ReadByte());

        Assert.Equal(8, stream.Seek(-1, SeekOrigin.End));
        Assert.Equal(8, stream.Position);
        Assert.Equal(stream.Position + 1, stream.ReadByte());

        Assert.Equal(5, stream.Seek(-4, SeekOrigin.End));
        Assert.Equal(5, stream.Position);
        Assert.Equal(stream.Position + 1, stream.ReadByte());

        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(1, SeekOrigin.End));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin));

        stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void ReadByte()
    {
        var stream = MultiBlockSequence.AsStream();

        for (int i = 0; i < MultiBlockSequence.Length; i++)
        {
            Assert.Equal(i + 1, stream.ReadByte());
        }

        Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public void Read()
    {
        var stream = MultiBlockSequence.AsStream();
        var buffer = new byte[MultiBlockSequence.Length + 2];
        Assert.Equal(2, stream.Read(buffer, 0, 2));
        Assert.Equal(new byte[] { 1, 2, 0 }, buffer.Take(3));
        Assert.Equal(2, stream.Position);

        Assert.Equal(2, stream.Read(buffer, 3, 2));
        Assert.Equal(new byte[] { 1, 2, 0, 3, 4, 0 }, buffer.Take(6));

        Assert.Equal(5, stream.Read(buffer, 5, buffer.Length - 5));
        Assert.Equal(new byte[] { 1, 2, 0, 3, 4, 5, 6, 7, 8, 9, 0 }, buffer);
        Assert.Equal(9, stream.Position);

        Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(9, stream.Position);
    }

#if NETCOREAPP2_1

    [Fact]
    public void Read_Span()
    {
        var stream = MultiBlockSequence.AsStream();
        var buffer = new byte[MultiBlockSequence.Length + 2];
        Assert.Equal(2, stream.Read(buffer.AsSpan(0, 2)));
        Assert.Equal(new byte[] { 1, 2, 0 }, buffer.Take(3));
        Assert.Equal(2, stream.Position);

        Assert.Equal(2, stream.Read(buffer.AsSpan(3, 2)));
        Assert.Equal(new byte[] { 1, 2, 0, 3, 4, 0 }, buffer.Take(6));

        Assert.Equal(5, stream.Read(buffer.AsSpan(5, buffer.Length - 5)));
        Assert.Equal(new byte[] { 1, 2, 0, 3, 4, 5, 6, 7, 8, 9, 0 }, buffer);
        Assert.Equal(9, stream.Position);

        Assert.Equal(0, stream.Read(buffer.AsSpan(0, buffer.Length)));
        Assert.Equal(0, stream.Read(buffer.AsSpan(0, buffer.Length)));
        Assert.Equal(9, stream.Position);
    }

#endif

    [Fact]
    public void ReadAsync_ReturnsSynchronously()
    {
        var stream = SimpleSequence.AsStream();
        Assert.True(stream.ReadAsync(new byte[1], 0, 1).IsCompleted);
    }

    [Fact]
    public async Task ReadAsync_ReusesTaskResult()
    {
        var stream = MultiBlockSequence.AsStream();
        Task<int> task1 = stream.ReadAsync(new byte[1], 0, 1);
        Task<int> task2 = stream.ReadAsync(new byte[1], 0, 1);
        Assert.Same(task1, task2);
        Assert.Equal(1, await task1);

        Task<int> task3 = stream.ReadAsync(new byte[2], 0, 2);
        Task<int> task4 = stream.ReadAsync(new byte[2], 0, 2);
        Assert.Same(task3, task4);
        Assert.Equal(2, await task3);
    }

    [Fact]
    public async Task ReadAsync_Works()
    {
        var stream = MultiBlockSequence.AsStream();
        var buffer = new byte[MultiBlockSequence.Length + 2];
        Assert.Equal(2, await stream.ReadAsync(buffer, 0, 2));
        Assert.Equal(new byte[] { 1, 2, 0 }, buffer.Take(3));
        Assert.Equal(2, stream.Position);

        Assert.Equal(2, await stream.ReadAsync(buffer, 3, 2));
        Assert.Equal(new byte[] { 1, 2, 0, 3, 4, 0 }, buffer.Take(6));

        Assert.Equal(5, await stream.ReadAsync(buffer, 5, buffer.Length - 5));
        Assert.Equal(new byte[] { 1, 2, 0, 3, 4, 5, 6, 7, 8, 9, 0 }, buffer);
        Assert.Equal(9, stream.Position);

        Assert.Equal(0, await stream.ReadAsync(buffer, 0, buffer.Length));
        Assert.Equal(0, await stream.ReadAsync(buffer, 0, buffer.Length));
        Assert.Equal(9, stream.Position);
    }

#if NETCOREAPP2_1

    [Fact]
    public async Task ReadAsync_Memory_Works()
    {
        var stream = MultiBlockSequence.AsStream();
        var buffer = new byte[MultiBlockSequence.Length + 2];
        Assert.Equal(2, await stream.ReadAsync(buffer.AsMemory(0, 2)));
        Assert.Equal(new byte[] { 1, 2, 0 }, buffer.Take(3));
        Assert.Equal(2, stream.Position);

        Assert.Equal(2, await stream.ReadAsync(buffer.AsMemory(3, 2)));
        Assert.Equal(new byte[] { 1, 2, 0, 3, 4, 0 }, buffer.Take(6));

        Assert.Equal(5, await stream.ReadAsync(buffer.AsMemory(5, buffer.Length - 5)));
        Assert.Equal(new byte[] { 1, 2, 0, 3, 4, 5, 6, 7, 8, 9, 0 }, buffer);
        Assert.Equal(9, stream.Position);

        Assert.Equal(0, await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)));
        Assert.Equal(0, await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)));
        Assert.Equal(9, stream.Position);
    }

#endif

    [Fact]
    public async Task CopyToAsync()
    {
        var stream = MultiBlockSequence.AsStream();
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(MultiBlockSequence.ToArray(), ms.ToArray());
    }

    private class SeqSegment : ReadOnlySequenceSegment<byte>
    {
        public SeqSegment(byte[] buffer, SeqSegment next)
        {
            this.Memory = buffer;
            this.Next = next;

            SeqSegment current = this;
            while (next != null)
            {
                next.RunningIndex = current.RunningIndex + current.Memory.Length;
                current = next;
                next = (SeqSegment)next.Next;
            }
        }
    }
}
