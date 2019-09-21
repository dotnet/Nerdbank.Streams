// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class MonitoringStreamTests : TestBase
{
    private MemoryStream underlyingStream;

    private MonitoringStream monitoringStream;

    private byte[] buffer;

    public MonitoringStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.underlyingStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        this.monitoringStream = new MonitoringStream(this.underlyingStream);
        this.buffer = new byte[] { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
    }

    [Fact]
    public void Ctor_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => new MonitoringStream(null!));
    }

    [Fact]
    public async Task OperationsWithNoEvents()
    {
        this.monitoringStream.Read(this.buffer, 0, 1);
        await this.monitoringStream.ReadAsync(this.buffer, 0, 1);
        this.monitoringStream.ReadByte();

        this.monitoringStream.Seek(0, SeekOrigin.Begin);

        this.monitoringStream.Write(this.buffer, 0, 1);
        await this.monitoringStream.WriteAsync(this.buffer, 0, 1);
        this.monitoringStream.WriteByte(1);

        this.monitoringStream.SetLength(0);

        this.monitoringStream.Dispose();
    }

    [Fact]
    public void Read_RaisesEvents()
    {
        bool willReadInvoked = false;
        bool didReadInvoked = false;
        this.monitoringStream.WillRead += (s, e) =>
        {
            willReadInvoked = true;
            Assert.False(didReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Same(this.buffer, e.Array);
            Assert.Equal(2, e.Offset);
            Assert.Equal(6, e.Count);
        };
        this.monitoringStream.DidRead += (s, e) =>
        {
            didReadInvoked = true;
            Assert.True(willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Same(this.buffer, e.Array);
            Assert.Equal(2, e.Offset);
            Assert.Equal(5, e.Count);
        };
        int bytesRead = this.monitoringStream.Read(this.buffer, 2, 6);
        Assert.Equal(5, bytesRead);
        Assert.Equal(bytesRead, this.underlyingStream.Position);
        Assert.True(willReadInvoked);
        Assert.True(didReadInvoked);
    }

    [Fact]
    public async Task ReadAsync_RaisesEvents()
    {
        bool willReadInvoked = false;
        bool didReadInvoked = false;
        this.monitoringStream.WillRead += (s, e) =>
        {
            willReadInvoked = true;
            Assert.False(didReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Same(this.buffer, e.Array);
            Assert.Equal(2, e.Offset);
            Assert.Equal(6, e.Count);
        };
        this.monitoringStream.DidRead += (s, e) =>
        {
            didReadInvoked = true;
            Assert.True(willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Same(this.buffer, e.Array);
            Assert.Equal(2, e.Offset);
            Assert.Equal(5, e.Count);
        };
        int bytesRead = await this.monitoringStream.ReadAsync(this.buffer, 2, 6);
        Assert.Equal(5, bytesRead);
        Assert.Equal(bytesRead, this.underlyingStream.Position);
        Assert.True(willReadInvoked);
        Assert.True(didReadInvoked);
    }

    [Fact]
    public void ReadByte_RaisesEvents()
    {
        bool willReadInvoked = false;
        bool didReadInvoked = false;
        this.monitoringStream.WillReadByte += (s, e) =>
        {
            willReadInvoked = true;
            Assert.False(didReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.NotNull(e);
        };
        this.monitoringStream.DidReadByte += (s, e) =>
        {
            didReadInvoked = true;
            Assert.True(willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(1, e);
        };
        int byteRead = this.monitoringStream.ReadByte();
        Assert.Equal(1, byteRead);
        Assert.Equal(1, this.underlyingStream.Position);
        Assert.True(willReadInvoked);
        Assert.True(didReadInvoked);
    }

    [Fact]
    public void Write_RaisesEvents()
    {
        bool willWriteInvoked = false;
        bool didWriteInvoked = false;
        this.monitoringStream.WillWrite += (s, e) =>
        {
            willWriteInvoked = true;
            Assert.False(didWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Same(this.buffer, e.Array);
            Assert.Equal(2, e.Offset);
            Assert.Equal(3, e.Count);
        };
        this.monitoringStream.DidWrite += (s, e) =>
        {
            didWriteInvoked = true;
            Assert.True(willWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Same(this.buffer, e.Array);
            Assert.Equal(2, e.Offset);
            Assert.Equal(3, e.Count);
        };
        this.monitoringStream.Write(this.buffer, 2, 3);
        Assert.True(willWriteInvoked);
        Assert.True(didWriteInvoked);
        Assert.Equal(new byte[] { 8, 9, 10, 4, 5 }, this.underlyingStream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_RaisesEvents()
    {
        bool willWriteInvoked = false;
        bool didWriteInvoked = false;
        this.monitoringStream.WillWrite += (s, e) =>
        {
            willWriteInvoked = true;
            Assert.False(didWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Same(this.buffer, e.Array);
            Assert.Equal(2, e.Offset);
            Assert.Equal(3, e.Count);
        };
        this.monitoringStream.DidWrite += (s, e) =>
        {
            didWriteInvoked = true;
            Assert.True(willWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Same(this.buffer, e.Array);
            Assert.Equal(2, e.Offset);
            Assert.Equal(3, e.Count);
        };
        await this.monitoringStream.WriteAsync(this.buffer, 2, 3);
        Assert.True(willWriteInvoked);
        Assert.True(didWriteInvoked);
        Assert.Equal(new byte[] { 8, 9, 10, 4, 5 }, this.underlyingStream.ToArray());
    }

    [Fact]
    public void WriteByte_RaisesEvents()
    {
        bool willWriteInvoked = false;
        bool didWriteInvoked = false;
        this.monitoringStream.WillWriteByte += (s, e) =>
        {
            willWriteInvoked = true;
            Assert.False(didWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(11, e);
        };
        this.monitoringStream.DidWriteByte += (s, e) =>
        {
            didWriteInvoked = true;
            Assert.True(willWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(11, e);
        };
        this.monitoringStream.WriteByte(11);
        Assert.True(willWriteInvoked);
        Assert.True(didWriteInvoked);
        Assert.Equal(11, this.underlyingStream.ToArray()[0]);
    }

    [Fact]
    public void Seek_RaisesEvents()
    {
        bool didSeekInvoked = false;
        this.monitoringStream.DidSeek += (s, e) =>
        {
            didSeekInvoked = true;
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(2, e);
        };
        this.monitoringStream.Seek(2, SeekOrigin.Begin);
        Assert.Equal(2, this.underlyingStream.Position);
        Assert.True(didSeekInvoked);
    }

    [Fact]
    public void SetLength_RaisesEvents()
    {
        bool willSetLengthInvoked = false;
        bool didSetLengthInvoked = false;
        this.monitoringStream.WillSetLength += (s, e) =>
        {
            willSetLengthInvoked = true;
            Assert.False(didSetLengthInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(2, e);
        };
        this.monitoringStream.DidSetLength += (s, e) =>
        {
            didSetLengthInvoked = true;
            Assert.True(willSetLengthInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(2, e);
        };
        this.monitoringStream.SetLength(2);
        Assert.Equal(2, this.underlyingStream.Length);
        Assert.True(didSetLengthInvoked);
        Assert.True(willSetLengthInvoked);
    }

    [Fact]
    public void Dispose_RaisesEvent()
    {
        bool disposeInvoked = false;
        this.monitoringStream.Disposed += (s, e) =>
        {
            disposeInvoked = true;
            Assert.Same(this.monitoringStream, s);
            Assert.NotNull(e);
        };
        this.monitoringStream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.underlyingStream.Seek(0, SeekOrigin.Begin));
        Assert.True(disposeInvoked);
    }

    [Fact]
    public void CanSeek()
    {
        Assert.True(this.monitoringStream.CanSeek);
        this.underlyingStream.Dispose();
        Assert.False(this.monitoringStream.CanSeek);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.True(this.monitoringStream.CanWrite);
        this.underlyingStream.Dispose();
        Assert.False(this.monitoringStream.CanWrite);
    }

    [Fact]
    public void CanRead()
    {
        Assert.True(this.monitoringStream.CanRead);
        this.underlyingStream.Dispose();
        Assert.False(this.monitoringStream.CanRead);
    }

    [Fact]
    public void CanTimeout()
    {
        var mockedUnderlyingStream = new Mock<Stream>(MockBehavior.Strict);
        mockedUnderlyingStream.SetupGet(s => s.CanTimeout).Returns(true);
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream.Object);
        Assert.True(monitoringStream.CanTimeout);
        mockedUnderlyingStream.SetupGet(s => s.CanTimeout).Returns(false);
        Assert.False(monitoringStream.CanTimeout);
    }

    [Fact]
    public void ReadTimeout()
    {
        var mockedUnderlyingStream = new Mock<Stream>(MockBehavior.Strict);
        mockedUnderlyingStream.SetupProperty(s => s.ReadTimeout);
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream.Object);
        Assert.Equal(mockedUnderlyingStream.Object.ReadTimeout, monitoringStream.ReadTimeout);
        monitoringStream.ReadTimeout = 13;
        Assert.Equal(mockedUnderlyingStream.Object.ReadTimeout, monitoringStream.ReadTimeout);
        Assert.Equal(13, mockedUnderlyingStream.Object.ReadTimeout);
    }

    [Fact]
    public void WriteTimeout()
    {
        var mockedUnderlyingStream = new Mock<Stream>(MockBehavior.Strict);
        mockedUnderlyingStream.SetupProperty(s => s.WriteTimeout);
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream.Object);
        Assert.Equal(mockedUnderlyingStream.Object.WriteTimeout, monitoringStream.WriteTimeout);
        monitoringStream.WriteTimeout = 13;
        Assert.Equal(mockedUnderlyingStream.Object.WriteTimeout, monitoringStream.WriteTimeout);
        Assert.Equal(13, mockedUnderlyingStream.Object.WriteTimeout);
    }

    [Fact]
    public void Flush()
    {
        var mockedUnderlyingStream = new Mock<Stream>(MockBehavior.Strict);
        mockedUnderlyingStream.Setup(s => s.Flush());
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream.Object);
        monitoringStream.Flush();
        mockedUnderlyingStream.VerifyAll();
    }

    [Fact]
    public async Task FlushAsync()
    {
        var mockedUnderlyingStream = new Mock<Stream>(MockBehavior.Strict);
        mockedUnderlyingStream.Setup(s => s.FlushAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream.Object);
        await monitoringStream.FlushAsync();
        mockedUnderlyingStream.VerifyAll();
    }

    [Fact]
    public void Length()
    {
        Assert.Equal(this.underlyingStream.Length, this.monitoringStream.Length);
    }

    [Fact]
    public void Position()
    {
        Assert.Equal(this.underlyingStream.Position, this.monitoringStream.Position);
        this.monitoringStream.Position++;
        Assert.Equal(this.underlyingStream.Position, this.monitoringStream.Position);
        Assert.Equal(1, this.monitoringStream.Position);
    }
}
