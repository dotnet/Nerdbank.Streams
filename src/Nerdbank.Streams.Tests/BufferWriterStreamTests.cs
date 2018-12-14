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

public class BufferWriterStreamTests : TestBase
{
    private Sequence<byte> sequence;

    private Stream stream;

    public BufferWriterStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.sequence = new Sequence<byte>();
        this.stream = this.sequence.AsStream();
    }

    [Fact]
    public void Null_Throws()
    {
        IBufferWriter<byte> writer = null;
        Assert.Throws<ArgumentNullException>(() => writer.AsStream());
    }

    [Fact]
    public void Length()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Length);
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Length);
    }

    [Fact]
    public void SetLength()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.SetLength(0));
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.SetLength(0));
    }

    [Fact]
    public void CanSeek()
    {
        Assert.False(this.stream.CanSeek);
        this.stream.Dispose();
        Assert.False(this.stream.CanSeek);
    }

    [Fact]
    public void CanRead()
    {
        Assert.False(this.stream.CanRead);
        this.stream.Dispose();
        Assert.False(this.stream.CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.True(this.stream.CanWrite);
        this.stream.Dispose();
        Assert.False(this.stream.CanWrite);
    }

    [Fact]
    public void CanTimeout()
    {
        Assert.False(this.stream.CanTimeout);
        this.stream.Dispose();
        Assert.False(this.stream.CanTimeout);
    }

    [Fact]
    public void Position()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => this.stream.Position);
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => this.stream.Position);
    }

    [Fact]
    public void IsDisposed()
    {
        Assert.False(((IDisposableObservable)this.stream).IsDisposed);
        this.stream.Dispose();
        Assert.True(((IDisposableObservable)this.stream).IsDisposed);
    }

    [Fact]
    public void Flush()
    {
        this.stream.Flush(); // no-op shouldn't throw.
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Flush());
    }

    [Fact]
    public async Task FlushAsync()
    {
        await this.stream.FlushAsync(); // no-op shouldn't throw.
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.FlushAsync());
    }

    [Fact]
    public async Task Write_Disposed()
    {
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Write(new byte[1], 0, 1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.WriteAsync(new byte[1], 0, 1));
        Assert.Throws<ObjectDisposedException>(() => this.stream.WriteByte(1));
    }

    [Fact]
    public async Task Write_BadArgs()
    {
        Assert.Throws<ArgumentNullException>(() => this.stream.Write(null, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Write(new byte[1], 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Write(new byte[1], -1, 1));

        await Assert.ThrowsAsync<ArgumentNullException>(() => this.stream.WriteAsync(null, 0, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.stream.WriteAsync(new byte[1], 0, 2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.stream.WriteAsync(new byte[1], -1, 1));
    }

    [Fact]
    public void Write()
    {
        this.stream.Write(new byte[] { 1, 2, 3, 4, 5 }, 1, 3);
        Assert.Equal(new byte[] { 2, 3, 4 }, this.sequence.AsReadOnlySequence.ToArray());
        this.stream.Write(new byte[] { 5, 6 }, 0, 2);
        Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, this.sequence.AsReadOnlySequence.ToArray());

        this.stream.Write(new byte[0], 0, 0);
        Assert.Equal(5, this.sequence.AsReadOnlySequence.Length);
    }

#if NETCOREAPP2_1

    [Fact]
    public void Write_Span()
    {
        this.stream.Write(new byte[] { 1, 2, 3, 4, 5 }.AsSpan(1, 3));
        Assert.Equal(new byte[] { 2, 3, 4 }, this.sequence.AsReadOnlySequence.ToArray());
        this.stream.Write(new byte[] { 5, 6 }.AsSpan(0, 2));
        Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, this.sequence.AsReadOnlySequence.ToArray());

        this.stream.Write(new byte[0].AsSpan(0, 0));
        Assert.Equal(5, this.sequence.AsReadOnlySequence.Length);
    }

#endif

    [Fact]
    public void WriteAsync_ReturnsSynchronously()
    {
        Assert.True(this.stream.WriteAsync(new byte[] { 1, 2, 3, 4, 5 }, 1, 3).IsCompleted);
    }

    [Fact]
    public async Task WriteAsync()
    {
        await this.stream.WriteAsync(new byte[] { 1, 2, 3, 4, 5 }, 1, 3);
        Assert.Equal(new byte[] { 2, 3, 4 }, this.sequence.AsReadOnlySequence.ToArray());
        await this.stream.WriteAsync(new byte[] { 5, 6 }, 0, 2);
        Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, this.sequence.AsReadOnlySequence.ToArray());

        await this.stream.WriteAsync(new byte[0], 0, 0);
        Assert.Equal(5, this.sequence.AsReadOnlySequence.Length);
    }

#if NETCOREAPP2_1

    [Fact]
    public async Task WriteAsync_Memory()
    {
        await this.stream.WriteAsync(new byte[] { 1, 2, 3, 4, 5 }.AsMemory(1, 3));
        Assert.Equal(new byte[] { 2, 3, 4 }, this.sequence.AsReadOnlySequence.ToArray());
        await this.stream.WriteAsync(new byte[] { 5, 6 }.AsMemory(0, 2));
        Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, this.sequence.AsReadOnlySequence.ToArray());

        await this.stream.WriteAsync(new byte[0].AsMemory(0, 0));
        Assert.Equal(5, this.sequence.AsReadOnlySequence.Length);
    }

#endif

    [Fact]
    public void WriteByte()
    {
        this.stream.WriteByte(1);
        Assert.Equal(new byte[] { 1 }, this.sequence.AsReadOnlySequence.ToArray());
        this.stream.WriteByte(2);
        Assert.Equal(new byte[] { 1, 2 }, this.sequence.AsReadOnlySequence.ToArray());
    }

    [Fact]
    public void Seek_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Seek(0, SeekOrigin.Begin));
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void ReadByte_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.ReadByte());
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.ReadByte());
    }

    [Fact]
    public void Read_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Read(new byte[1], 0, 1));
#if NETCOREAPP2_1
        Assert.Throws<NotSupportedException>(() => this.stream.Read(new byte[1].AsSpan(0, 1)));
#endif
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Read(new byte[1], 0, 1));
#if NETCOREAPP2_1
        Assert.Throws<ObjectDisposedException>(() => this.stream.Read(new byte[1].AsSpan(0, 1)));
#endif
    }

    [Fact]
    public async Task ReadAsync_IsNotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => this.stream.ReadAsync(new byte[1], 0, 1));
#if NETCOREAPP2_1
        await Assert.ThrowsAsync<NotSupportedException>(() => this.stream.ReadAsync(new byte[1].AsMemory(0, 1)).AsTask());
#endif
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.ReadAsync(new byte[1], 0, 1));
#if NETCOREAPP2_1
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.ReadAsync(new byte[1].AsMemory(0, 1)).AsTask());
#endif
    }
}
