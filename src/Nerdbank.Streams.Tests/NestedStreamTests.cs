// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using Moq;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class NestedStreamTests : TestBase
{
    private const int DefaultNestedLength = 10;

    private MemoryStream underlyingStream;

    private Stream stream;

    public NestedStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        var random = new Random();
        var buffer = new byte[20];
        random.NextBytes(buffer);
        this.underlyingStream = new MemoryStream(buffer);
        this.stream = this.underlyingStream.ReadSlice(DefaultNestedLength);
    }

    [Fact]
    public void Slice_InputValidation()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.ReadSlice(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StreamExtensions.ReadSlice(new MemoryStream(), -1));

        var noReadStream = new Mock<Stream>(MockBehavior.Strict);
        noReadStream.SetupGet(s => s.CanRead).Returns(false);
        Assert.Throws<ArgumentException>(() => StreamExtensions.ReadSlice(noReadStream.Object, 1));
    }

    [Fact]
    public void CanSeek()
    {
        Assert.True(this.stream.CanSeek);
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.CanSeek);
    }

    [Fact]
    public void Length()
    {
        Assert.Equal(DefaultNestedLength, this.stream.Length);
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Length);
    }

    [Fact]
    public void Position()
    {
        byte[] buffer = new byte[DefaultNestedLength];

        Assert.Equal(0, this.stream.Position);
        var bytesRead = this.stream.Read(buffer, 0, 5);
        Assert.Equal(bytesRead, this.stream.Position);

        Assert.Throws<NotSupportedException>(() => this.stream.Position = 0);
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Position);
        Assert.Throws<ObjectDisposedException>(() => this.stream.Position = 0);
    }

    [Fact]
    public void IsDisposed()
    {
        Assert.False(((IDisposableObservable)this.stream).IsDisposed);
        this.stream.Dispose();
        Assert.True(((IDisposableObservable)this.stream).IsDisposed);
    }

    [Fact]
    public void Dispose_DoesNotDisposeUnderylingStream()
    {
        this.stream.Dispose();
        Assert.True(this.underlyingStream.CanSeek);

        // A sanity check that if it were disposed, our assertion above would fail.
        this.underlyingStream.Dispose();
        Assert.False(this.underlyingStream.CanSeek);
    }

    [Fact]
    public void SetLength()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.SetLength(0));
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.SetLength(0));
    }

    [Fact]
    public void Seek_Current()
    {
        Assert.Equal(0, this.stream.Position);
        Assert.Equal(0, this.stream.Seek(0, SeekOrigin.Current));
        Assert.Equal(0, this.underlyingStream.Position);
        Assert.Equal(0, this.stream.Seek(-1, SeekOrigin.Current));
        Assert.Equal(0, this.underlyingStream.Position);

        Assert.Equal(5, this.stream.Seek(5, SeekOrigin.Current));
        Assert.Equal(5, this.underlyingStream.Position);
        Assert.Equal(5, this.stream.Seek(0, SeekOrigin.Current));
        Assert.Equal(5, this.underlyingStream.Position);
        Assert.Equal(4, this.stream.Seek(-1, SeekOrigin.Current));
        Assert.Equal(4, this.underlyingStream.Position);
        Assert.Equal(0, this.stream.Seek(-10, SeekOrigin.Current));
        Assert.Equal(0, this.underlyingStream.Position);

        Assert.Equal(10, this.stream.Seek(10, SeekOrigin.Current));
        Assert.Equal(10, this.underlyingStream.Position);
        Assert.Equal(10, this.stream.Seek(10, SeekOrigin.Current));
        Assert.Equal(10, this.underlyingStream.Position);
        Assert.Equal(10, this.stream.Seek(0, SeekOrigin.Current));
        Assert.Equal(10, this.underlyingStream.Position);
        Assert.Equal(0, this.stream.Seek(-20, SeekOrigin.Current));
        Assert.Equal(0, this.underlyingStream.Position);

        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_Begin()
    {
        Assert.Equal(0, this.stream.Position);
        Assert.Equal(0, this.stream.Seek(-1, SeekOrigin.Begin));
        Assert.Equal(0, this.underlyingStream.Position);

        Assert.Equal(0, this.stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(0, this.underlyingStream.Position);

        Assert.Equal(5, this.stream.Seek(5, SeekOrigin.Begin));
        Assert.Equal(5, this.underlyingStream.Position);

        Assert.Equal(10, this.stream.Seek(10, SeekOrigin.Begin));
        Assert.Equal(10, this.underlyingStream.Position);

        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_End()
    {
        Assert.Equal(0, this.stream.Position);
        Assert.Equal(9, this.stream.Seek(-1, SeekOrigin.End));
        Assert.Equal(9, this.underlyingStream.Position);

        Assert.Equal(10, this.stream.Seek(0, SeekOrigin.End));
        Assert.Equal(10, this.underlyingStream.Position);

        Assert.Equal(10, this.stream.Seek(5, SeekOrigin.End));
        Assert.Equal(10, this.underlyingStream.Position);

        Assert.Equal(0, this.stream.Seek(-20, SeekOrigin.Begin));
        Assert.Equal(0, this.underlyingStream.Position);

        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Seek(0, SeekOrigin.End));
    }

    [Fact]
    public void Flush()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Flush());
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Flush());
    }

    [Fact]
    public async Task FlushAsync()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => this.stream.FlushAsync());
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.FlushAsync());
    }

    [Fact]
    public void CanRead()
    {
        Assert.True(this.stream.CanRead);
        this.stream.Dispose();
        Assert.False(this.stream.CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.False(this.stream.CanWrite);
        this.stream.Dispose();
        Assert.False(this.stream.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_Throws()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => this.stream.WriteAsync(new byte[1], 0, 1).WithCancellation(this.TimeoutToken));
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.WriteAsync(new byte[1], 0, 1).WithCancellation(this.TimeoutToken));
    }

    [Fact]
    public void Write_Throws()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Write(new byte[1], 0, 1));
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Write(new byte[1], 0, 1));
    }

    [Fact]
    public async Task ReadAsync_Empty_ReturnsZero()
    {
        Assert.Equal(0, await this.stream.ReadAsync(Array.Empty<byte>(), 0, 0, default).WithCancellation(this.TimeoutToken));

#if SPAN_BUILTIN
        Assert.Equal(0, await this.stream.ReadAsync(Array.Empty<byte>(), default).AsTask().WithCancellation(this.TimeoutToken));
#endif
    }

    [Fact]
    public async Task ReadAsync_NoMoreThanGiven()
    {
        byte[] buffer = new byte[this.underlyingStream.Length];
        int bytesRead = await this.stream.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(DefaultNestedLength, bytesRead);

        Assert.Equal(0, await this.stream.ReadAsync(buffer, bytesRead, buffer.Length - bytesRead, this.TimeoutToken).WithCancellation(this.TimeoutToken));
        Assert.Equal(DefaultNestedLength, this.underlyingStream.Position);
    }

    [Fact]
    public void Read_NoMoreThanGiven()
    {
        byte[] buffer = new byte[this.underlyingStream.Length];
        int bytesRead = this.stream.Read(buffer, 0, buffer.Length);
        Assert.Equal(DefaultNestedLength, bytesRead);

        Assert.Equal(0, this.stream.Read(buffer, bytesRead, buffer.Length - bytesRead));
        Assert.Equal(DefaultNestedLength, this.underlyingStream.Position);
    }

    [Fact]
    public void Read_Empty_ReturnsZero()
    {
        Assert.Equal(0, this.stream.Read(Array.Empty<byte>(), 0, 0));
    }

    [Fact]
    public async Task ReadAsync_WhenLengthIsInitially0()
    {
        this.stream = this.underlyingStream.ReadSlice(0);
        Assert.Equal(0, await this.stream.ReadAsync(new byte[1], 0, 1, this.TimeoutToken).WithCancellation(this.TimeoutToken));
    }

    [Fact]
    public void Read_WhenLengthIsInitially0()
    {
        this.stream = this.underlyingStream.ReadSlice(0);
        Assert.Equal(0, this.stream.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void CreationDoesNotReadFromUnderlyingStream()
    {
        Assert.Equal(0, this.underlyingStream.Position);
    }

    [Fact]
    public void Read_UnderlyingStreamReturnsFewerBytesThanRequested()
    {
        var buffer = new byte[20];
        int firstBlockLength = DefaultNestedLength / 2;
        this.underlyingStream.SetLength(firstBlockLength);
        Assert.Equal(firstBlockLength, this.stream.Read(buffer, 0, buffer.Length));
        this.underlyingStream.SetLength(DefaultNestedLength * 2);
        Assert.Equal(DefaultNestedLength - firstBlockLength, this.stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task ReadAsync_UnderlyingStreamReturnsFewerBytesThanRequested()
    {
        var buffer = new byte[20];
        int firstBlockLength = DefaultNestedLength / 2;
        this.underlyingStream.SetLength(firstBlockLength);
        Assert.Equal(firstBlockLength, await this.stream.ReadAsync(buffer, 0, buffer.Length));
        this.underlyingStream.SetLength(DefaultNestedLength * 2);
        Assert.Equal(DefaultNestedLength - firstBlockLength, await this.stream.ReadAsync(buffer, 0, buffer.Length));
    }
}
