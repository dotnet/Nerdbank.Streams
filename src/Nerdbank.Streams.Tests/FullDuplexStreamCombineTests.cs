// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Moq;
using Nerdbank.Streams;
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
        Assert.Throws<ArgumentNullException>(() => FullDuplexStream.Splice(null, null));
        Assert.Throws<ArgumentNullException>(() => FullDuplexStream.Splice(null, Stream.Null));
        Assert.Throws<ArgumentNullException>(() => FullDuplexStream.Splice(Stream.Null, null));
    }

    [Fact]
    public void JoinStreams_ReadableWritableMismatch()
    {
        var readableMock = new Mock<Stream>(MockBehavior.Strict);
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        readableMock.SetupGet(s => s.CanWrite).Returns(false);

        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanRead).Returns(false);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);

        Assert.Throws<ArgumentException>(() => FullDuplexStream.Splice(writableMock.Object, new MemoryStream()));
        Assert.Throws<ArgumentException>(() => FullDuplexStream.Splice(new MemoryStream(), readableMock.Object));
        FullDuplexStream.Splice(readableMock.Object, writableMock.Object);
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
        var readableMock = new Mock<Stream>();
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        var writableMock = new Mock<Stream>();
        writableMock.SetupGet(s => s.CanWrite).Returns(true);

        var duplex = FullDuplexStream.Splice(readableMock.Object, writableMock.Object);
        Assert.False(duplex.CanTimeout);

        readableMock.SetupGet(s => s.CanTimeout).Returns(true);
        Assert.True(duplex.CanTimeout);

        readableMock.SetupGet(s => s.CanTimeout).Returns(false);
        writableMock.SetupGet(s => s.CanTimeout).Returns(true);
        Assert.True(duplex.CanTimeout);

        writableMock.SetupGet(s => s.CanTimeout).Returns(false);
        Assert.False(duplex.CanTimeout);

        duplex.Dispose();
        Assert.False(duplex.CanTimeout);
    }

    [Fact]
    public void ReadTimeout()
    {
        var readableMock = new Mock<Stream>();
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        readableMock.SetupProperty(s => s.ReadTimeout);
        var duplex = FullDuplexStream.Splice(readableMock.Object, Stream.Null);
        duplex.ReadTimeout = 8;
        Assert.Equal(8, duplex.ReadTimeout);
        Assert.Equal(8, readableMock.Object.ReadTimeout);
    }

    [Fact]
    public void WriteTimeout()
    {
        var writableMock = new Mock<Stream>();
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        writableMock.SetupProperty(s => s.WriteTimeout);
        var duplex = FullDuplexStream.Splice(Stream.Null, writableMock.Object);
        duplex.WriteTimeout = 8;
        Assert.Equal(8, duplex.WriteTimeout);
        Assert.Equal(8, writableMock.Object.WriteTimeout);
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
        var readableMock = new Mock<Stream>(MockBehavior.Strict);
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        var buffer = new byte[3];
        readableMock.Setup(s => s.Read(buffer, 2, 1)).Returns(1);
        var duplex = FullDuplexStream.Splice(readableMock.Object, Stream.Null);
        Assert.Equal(1, duplex.Read(buffer, 2, 1));
        readableMock.VerifyAll();
    }

    [Fact]
    public void ReadAsync_PassesThrough()
    {
        var readableMock = new Mock<Stream>(MockBehavior.Strict);
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        var buffer = new byte[3];
        var tcs = new TaskCompletionSource<int>();
        var cts = new CancellationTokenSource();
        readableMock.Setup(s => s.ReadAsync(buffer, 2, 1, cts.Token)).Returns(tcs.Task);
        var duplex = FullDuplexStream.Splice(readableMock.Object, Stream.Null);
        Assert.Same(tcs.Task, duplex.ReadAsync(buffer, 2, 1, cts.Token));
        readableMock.VerifyAll();
    }

    [Fact]
    public void ReadByte()
    {
        var readableMock = new Mock<Stream>(MockBehavior.Strict);
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        readableMock.Setup(s => s.ReadByte()).Returns(8);
        var duplex = FullDuplexStream.Splice(readableMock.Object, Stream.Null);
        Assert.Equal(8, duplex.ReadByte());
        readableMock.VerifyAll();
    }

    [Fact]
    public void Write()
    {
        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        var buffer = new byte[3];
        writableMock.Setup(s => s.Write(buffer, 2, 1));
        var duplex = FullDuplexStream.Splice(Stream.Null, writableMock.Object);
        duplex.Write(buffer, 2, 1);
        writableMock.VerifyAll();
    }

    [Fact]
    public void WriteAsync()
    {
        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        var buffer = new byte[3];
        var tcs = new TaskCompletionSource<object>();
        var cts = new CancellationTokenSource();
        writableMock.Setup(s => s.WriteAsync(buffer, 2, 1, cts.Token)).Returns(tcs.Task);
        var duplex = FullDuplexStream.Splice(Stream.Null, writableMock.Object);
        Assert.Same(tcs.Task, duplex.WriteAsync(buffer, 2, 1, cts.Token));
        writableMock.VerifyAll();
    }

#if NETCOREAPP2_1
    [Fact]
    public void ReadAsync_Memory()
    {
        var readableMock = new Mock<Stream>(MockBehavior.Strict);
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        Memory<byte> buffer = new byte[3];
        var task = new ValueTask<int>(new TaskCompletionSource<int>().Task);
        var cts = new CancellationTokenSource();
        readableMock.Setup(s => s.ReadAsync(buffer, cts.Token)).Returns(task);
        var duplex = FullDuplexStream.Splice(readableMock.Object, Stream.Null);
        Assert.Equal(task, duplex.ReadAsync(buffer, cts.Token));
        readableMock.VerifyAll();
    }

    [Fact]
    public void WriteAsync_ReadOnlyMemory()
    {
        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        var buffer = new ReadOnlyMemory<byte>(new byte[3]);
        var task = new ValueTask(new TaskCompletionSource<object>().Task);
        var cts = new CancellationTokenSource();
        writableMock.Setup(s => s.WriteAsync(buffer, cts.Token)).Returns(task);
        var duplex = FullDuplexStream.Splice(Stream.Null, writableMock.Object);
        Assert.Equal(task, duplex.WriteAsync(buffer, cts.Token));
        writableMock.VerifyAll();
    }

    [Fact(Skip = "Moq lacks support for testing ReadOnlySpan<T> overloads")]
    public void Write_ReadOnlySpan()
    {
        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        var buffer = new byte[3];
        writableMock.Setup(s => s.Write(new ReadOnlySpan<byte>(buffer)));
        var duplex = FullDuplexStream.Splice(Stream.Null, writableMock.Object);
        duplex.Write(buffer);
        writableMock.VerifyAll();
    }
#endif

    [Fact]
    public void WriteByte()
    {
        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        var buffer = new byte[3];
        writableMock.Setup(s => s.WriteByte(5));
        var duplex = FullDuplexStream.Splice(Stream.Null, writableMock.Object);
        duplex.WriteByte(5);
        writableMock.VerifyAll();
    }

    [Fact]
    public void Flush()
    {
        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        var buffer = new byte[3];
        var cts = new CancellationTokenSource();
        writableMock.Setup(s => s.Flush());
        var duplex = FullDuplexStream.Splice(Stream.Null, writableMock.Object);
        duplex.Flush();
        writableMock.VerifyAll();
    }

    [Fact]
    public void FlushAsync()
    {
        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        var buffer = new byte[3];
        var tcs = new TaskCompletionSource<object>();
        var cts = new CancellationTokenSource();
        writableMock.Setup(s => s.FlushAsync(cts.Token)).Returns(tcs.Task);
        var duplex = FullDuplexStream.Splice(Stream.Null, writableMock.Object);
        Assert.Same(tcs.Task, duplex.FlushAsync(cts.Token));
        writableMock.VerifyAll();
    }

    [Fact]
    public void IsDisposed()
    {
        Assert.False(((IDisposableObservable)this.nullDuplex).IsDisposed);
        this.nullDuplex.Dispose();
        Assert.True(((IDisposableObservable)this.nullDuplex).IsDisposed);
    }

    [Fact]
    public void CopyToAsync()
    {
        var readableMock = new Mock<Stream>(MockBehavior.Strict);
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        var ms = new MemoryStream();
        var tcs = new TaskCompletionSource<object>();
        var cts = new CancellationTokenSource();
        readableMock.Setup(s => s.CopyToAsync(ms, 867, cts.Token)).Returns(tcs.Task);
        var duplex = FullDuplexStream.Splice(readableMock.Object, Stream.Null);
        Assert.Same(tcs.Task, duplex.CopyToAsync(ms, 867, cts.Token));
        readableMock.VerifyAll();
    }

#if !NETCOREAPP1_0
    [Fact]
    public void Close()
    {
        var readableMock = new Mock<Stream>(MockBehavior.Strict);
        readableMock.SetupGet(s => s.CanRead).Returns(true);
        readableMock.Setup(s => s.Close());

        var writableMock = new Mock<Stream>(MockBehavior.Strict);
        writableMock.SetupGet(s => s.CanWrite).Returns(true);
        writableMock.Setup(s => s.Close());

        var duplex = FullDuplexStream.Splice(readableMock.Object, writableMock.Object);
        duplex.Close();
        readableMock.VerifyAll();
        writableMock.VerifyAll();
    }
#endif

    [Fact]
    public void Dispose_PassesThrough()
    {
        var readable = new MemoryStream();
        var writable = new MemoryStream();
        var duplex = FullDuplexStream.Splice(readable, writable);
        duplex.Dispose();

        Assert.Throws<ObjectDisposedException>(() => readable.Position);
        Assert.Throws<ObjectDisposedException>(() => writable.Position);
    }
}
