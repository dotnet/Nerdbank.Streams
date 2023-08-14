// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft;
using Nerdbank.Streams;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using Xunit;
using Xunit.Abstractions;

public class FullDuplexStreamCombineTests : TestBase
{
    private readonly Stream nullDuplex = FullDuplexStream.Splice(Stream.Null, Stream.Null);

    public FullDuplexStreamCombineTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void JoinStreams_NullArgs()
    {
        Assert.Throws<ArgumentNullException>(() => FullDuplexStream.Splice(null!, null!));
        Assert.Throws<ArgumentNullException>(() => FullDuplexStream.Splice(null!, Stream.Null));
        Assert.Throws<ArgumentNullException>(() => FullDuplexStream.Splice(Stream.Null, null!));
    }

    [Fact]
    public void JoinStreams_ReadableWritableMismatch()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);
        readableMock.CanWrite.Returns(false);

        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanRead.Returns(false);
        writableMock.CanWrite.Returns(true);

        Assert.Throws<ArgumentException>(() => FullDuplexStream.Splice(writableMock, new MemoryStream()));
        Assert.Throws<ArgumentException>(() => FullDuplexStream.Splice(new MemoryStream(), readableMock));
        FullDuplexStream.Splice(readableMock, writableMock);
    }

    [Fact]
    public void CanRead()
    {
        Assert.True(this.nullDuplex.CanRead);
        this.nullDuplex.Dispose();
        Assert.False(this.nullDuplex.CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.True(this.nullDuplex.CanWrite);
        this.nullDuplex.Dispose();
        Assert.False(this.nullDuplex.CanWrite);
    }

    [Fact]
    public void CanSeek()
    {
        Assert.False(this.nullDuplex.CanSeek);
        this.nullDuplex.Dispose();
        Assert.False(this.nullDuplex.CanSeek);
    }

    [Fact]
    public void Seek_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => this.nullDuplex.Seek(0, SeekOrigin.Begin));
        this.nullDuplex.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.nullDuplex.Seek(0, SeekOrigin.Begin));
    }

    /// <summary>
    /// Verifies that the duplex stream claims it can timeout if either or both half-duplex streams can.
    /// </summary>
    [Fact]
    public void CanTimeout()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);

        Stream? duplex = FullDuplexStream.Splice(readableMock, writableMock);
        Assert.False(duplex.CanTimeout);

        readableMock.CanTimeout.Returns(true);
        Assert.True(duplex.CanTimeout);

        readableMock.CanTimeout.Returns(false);
        writableMock.CanTimeout.Returns(true);
        Assert.True(duplex.CanTimeout);

        writableMock.CanTimeout.Returns(false);
        Assert.False(duplex.CanTimeout);

        duplex.Dispose();
        Assert.False(duplex.CanTimeout);
    }

    [Fact]
    public void ReadTimeout()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);
        Stream? duplex = FullDuplexStream.Splice(readableMock, Stream.Null);
        duplex.ReadTimeout = 8;
        Assert.Equal(8, duplex.ReadTimeout);
        Assert.Equal(8, readableMock.ReadTimeout);
    }

    [Fact]
    public void WriteTimeout()
    {
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);
        Stream? duplex = FullDuplexStream.Splice(Stream.Null, writableMock);
        duplex.WriteTimeout = 8;
        Assert.Equal(8, duplex.WriteTimeout);
        Assert.Equal(8, writableMock.WriteTimeout);
    }

    [Fact]
    public void Length()
    {
        Assert.Throws<NotSupportedException>(() => this.nullDuplex.Length);
        this.nullDuplex.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.nullDuplex.Length);
    }

    [Fact]
    public void SetLength()
    {
        Assert.Throws<NotSupportedException>(() => this.nullDuplex.SetLength(0));
        this.nullDuplex.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.nullDuplex.SetLength(0));
    }

    [Fact]
    public void Position()
    {
        Assert.Throws<NotSupportedException>(() => this.nullDuplex.Position = 0);
        Assert.Throws<NotSupportedException>(() => this.nullDuplex.Position);
        this.nullDuplex.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.nullDuplex.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => this.nullDuplex.Position);
    }

    [Fact]
    public void Read()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);
        byte[]? buffer = new byte[3];
        readableMock.Read(buffer, 2, 1).Returns(1);
        Stream? duplex = FullDuplexStream.Splice(readableMock, Stream.Null);
        Assert.Equal(1, duplex.Read(buffer, 2, 1));
        readableMock.Received().Read(buffer, 2, 1);
    }

    [Fact]
    public async Task ReadAsync_PassesThroughAsync()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);
        byte[]? buffer = new byte[3];
        var tcs = new TaskCompletionSource<int>();
        var cts = new CancellationTokenSource();
        readableMock.ReadAsync(buffer, 2, 1, cts.Token).Returns(tcs.Task);
        Stream? duplex = FullDuplexStream.Splice(readableMock, Stream.Null);
        Assert.Same(tcs.Task, duplex.ReadAsync(buffer, 2, 1, cts.Token));
        await readableMock.Received().ReadAsync(buffer, 2, 1, cts.Token);
    }

    [Fact]
    public void ReadByte()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);
        readableMock.ReadByte().Returns(8);
        Stream? duplex = FullDuplexStream.Splice(readableMock, Stream.Null);
        Assert.Equal(8, duplex.ReadByte());
        readableMock.Received().ReadByte();
    }

    [Fact]
    public void Write()
    {
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);
        byte[]? buffer = new byte[3];
        Stream? duplex = FullDuplexStream.Splice(Stream.Null, writableMock);
        duplex.Write(buffer, 2, 1);
        writableMock.Received().Write(buffer, 2, 1);
    }

    [Fact]
    public async Task WriteAsync()
    {
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);
        byte[]? buffer = new byte[3];
        var tcs = new TaskCompletionSource<object>();
        var cts = new CancellationTokenSource();
        writableMock.WriteAsync(buffer, 2, 1, cts.Token).Returns(tcs.Task);
        Stream? duplex = FullDuplexStream.Splice(Stream.Null, writableMock);
        Assert.Same(tcs.Task, duplex.WriteAsync(buffer, 2, 1, cts.Token));
        await writableMock.Received().WriteAsync(buffer, 2, 1, cts.Token);
    }

#if SPAN_BUILTIN
    [Fact]
    public async Task ReadAsync_Memory()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);
        Memory<byte> buffer = new byte[3];
        var task = new ValueTask<int>(new TaskCompletionSource<int>().Task);
        var cts = new CancellationTokenSource();
        readableMock.ReadAsync(buffer, cts.Token).Returns(task);
        Stream? duplex = FullDuplexStream.Splice(readableMock, Stream.Null);
        Assert.Equal(task, duplex.ReadAsync(buffer, cts.Token));
        await readableMock.Received().ReadAsync(buffer, cts.Token);
    }

    [Fact]
    public async Task WriteAsync_ReadOnlyMemory()
    {
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);
        var buffer = new ReadOnlyMemory<byte>(new byte[3]);
        var task = new ValueTask(new TaskCompletionSource<object>().Task);
        var cts = new CancellationTokenSource();
        writableMock.WriteAsync(buffer, cts.Token).Returns(task);
        Stream? duplex = FullDuplexStream.Splice(Stream.Null, writableMock);
        Assert.Equal(task, duplex.WriteAsync(buffer, cts.Token));
        await writableMock.Received().WriteAsync(buffer, cts.Token);
    }

    [Fact(Skip = "NSubstitute lacks support for testing ReadOnlySpan<T> overloads")]
    public void Write_ReadOnlySpan()
    {
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);
        var buffer = new byte[3];
        Stream duplex = FullDuplexStream.Splice(Stream.Null, writableMock);
        duplex.Write(buffer);
        writableMock.Received().Write(new ReadOnlySpan<byte>(buffer));
    }
#endif

    [Fact]
    public void WriteByte()
    {
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);
        byte[]? buffer = new byte[3];
        Stream? duplex = FullDuplexStream.Splice(Stream.Null, writableMock);
        duplex.WriteByte(5);
        writableMock.Received().WriteByte(5);
    }

    [Fact]
    public void Flush()
    {
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);
        byte[]? buffer = new byte[3];
        var cts = new CancellationTokenSource();
        Stream? duplex = FullDuplexStream.Splice(Stream.Null, writableMock);
        duplex.Flush();
        writableMock.Received().Flush();
    }

    [Fact]
    public async Task FlushAsync()
    {
        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);
        byte[]? buffer = new byte[3];
        var tcs = new TaskCompletionSource<object>();
        var cts = new CancellationTokenSource();
        writableMock.FlushAsync(cts.Token).Returns(tcs.Task);
        Stream? duplex = FullDuplexStream.Splice(Stream.Null, writableMock);
        Assert.Same(tcs.Task, duplex.FlushAsync(cts.Token));
        await writableMock.Received().FlushAsync(cts.Token);
    }

    [Fact]
    public void IsDisposed()
    {
        Assert.False(((IDisposableObservable)this.nullDuplex).IsDisposed);
        this.nullDuplex.Dispose();
        Assert.True(((IDisposableObservable)this.nullDuplex).IsDisposed);
    }

    [Fact]
    public async Task CopyToAsync()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);
        var ms = new MemoryStream();
        var tcs = new TaskCompletionSource<object>();
        var cts = new CancellationTokenSource();
        readableMock.CopyToAsync(ms, 867, cts.Token).Returns(tcs.Task);
        Stream? duplex = FullDuplexStream.Splice(readableMock, Stream.Null);
        Assert.Same(tcs.Task, duplex.CopyToAsync(ms, 867, cts.Token));
        await readableMock.Received().CopyToAsync(ms, 867, cts.Token);
    }

    [Fact]
    public void Close()
    {
        Stream readableMock = Substitute.For<Stream>();
        readableMock.CanRead.Returns(true);

        Stream writableMock = Substitute.For<Stream>();
        writableMock.CanWrite.Returns(true);

        Stream? duplex = FullDuplexStream.Splice(readableMock, writableMock);
        duplex.Close();
        readableMock.Received().Close();
        writableMock.Received().Close();
    }

    [Fact]
    public void Dispose_PassesThrough()
    {
        var readable = new MemoryStream();
        var writable = new MemoryStream();
        Stream? duplex = FullDuplexStream.Splice(readable, writable);
        duplex.Dispose();

        Assert.Throws<ObjectDisposedException>(() => readable.Position);
        Assert.Throws<ObjectDisposedException>(() => writable.Position);
    }
}
