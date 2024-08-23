﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Nerdbank.Streams;
using NSubstitute;
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
    public void Read_RaisesEndOfStream()
    {
        bool wasReadEndOfStreamInvoked = false;
        this.monitoringStream.EndOfStream += (s, e) =>
        {
            Assert.Same(this.monitoringStream, s);
            wasReadEndOfStreamInvoked = true;
        };

        int bytesRead = this.monitoringStream.Read(this.buffer, 0, this.buffer.Length);
        Assert.False(wasReadEndOfStreamInvoked);
        this.monitoringStream.Read(this.buffer, 0, this.buffer.Length);
        Assert.True(wasReadEndOfStreamInvoked);
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
    public async Task ReadAsync_RaisesEndOfStream()
    {
        bool wasReadEndOfStreamInvoked = false;
        this.monitoringStream.EndOfStream += (s, e) =>
        {
            Assert.Same(this.monitoringStream, s);
            wasReadEndOfStreamInvoked = true;
        };

        int bytesRead = await this.monitoringStream.ReadAsync(this.buffer, 0, this.buffer.Length);
        Assert.False(wasReadEndOfStreamInvoked);
        await this.monitoringStream.ReadAsync(this.buffer, 0, this.buffer.Length);
        Assert.True(wasReadEndOfStreamInvoked);
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
    public void ReadByte_RaisesEndOfStream()
    {
        bool wasReadEndOfStreamInvoked = false;
        this.monitoringStream.EndOfStream += (s, e) =>
        {
            Assert.Same(this.monitoringStream, s);
            wasReadEndOfStreamInvoked = true;
        };

        while (this.monitoringStream.ReadByte() != -1)
        {
            Assert.False(wasReadEndOfStreamInvoked);
        }

        Assert.True(wasReadEndOfStreamInvoked);
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
        Stream mockedUnderlyingStream = Substitute.For<Stream>();
        mockedUnderlyingStream.CanTimeout.Returns(true);
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream);
        Assert.True(monitoringStream.CanTimeout);
        mockedUnderlyingStream.CanTimeout.Returns(false);
        Assert.False(monitoringStream.CanTimeout);
    }

    [Fact]
    public void ReadTimeout()
    {
        Stream mockedUnderlyingStream = Substitute.For<Stream>();
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream);
        Assert.Equal(mockedUnderlyingStream.ReadTimeout, monitoringStream.ReadTimeout);
        monitoringStream.ReadTimeout = 13;
        Assert.Equal(mockedUnderlyingStream.ReadTimeout, monitoringStream.ReadTimeout);
        Assert.Equal(13, mockedUnderlyingStream.ReadTimeout);
    }

    [Fact]
    public void WriteTimeout()
    {
        Stream mockedUnderlyingStream = Substitute.For<Stream>();
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream);
        Assert.Equal(mockedUnderlyingStream.WriteTimeout, monitoringStream.WriteTimeout);
        monitoringStream.WriteTimeout = 13;
        Assert.Equal(mockedUnderlyingStream.WriteTimeout, monitoringStream.WriteTimeout);
        Assert.Equal(13, mockedUnderlyingStream.WriteTimeout);
    }

    [Fact]
    public void Flush()
    {
        Stream mockedUnderlyingStream = Substitute.For<Stream>();
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream);
        bool didFlushRaised = false;
        monitoringStream.DidFlush += (s, e) => didFlushRaised = true;
        monitoringStream.Flush();
        Assert.True(didFlushRaised);
        mockedUnderlyingStream.Received().Flush();
    }

    [Fact]
    public async Task FlushAsync()
    {
        Stream mockedUnderlyingStream = Substitute.For<Stream>();
        var monitoringStream = new MonitoringStream(mockedUnderlyingStream);
        bool didFlushRaised = false;
        monitoringStream.DidFlush += (s, e) => didFlushRaised = true;
        await monitoringStream.FlushAsync();
        Assert.True(didFlushRaised);
        await mockedUnderlyingStream.Received().FlushAsync(CancellationToken.None);
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

    [Fact]
    public void IsDisposed()
    {
        Assert.False(this.monitoringStream.IsDisposed);
        this.monitoringStream.Dispose();
        Assert.True(this.monitoringStream.IsDisposed);
    }

#if SPAN_BUILTIN

    [Fact]
    public void Read_Span_RaisesEvents()
    {
        bool willReadInvoked = false;
        bool didReadInvoked = false;
        this.monitoringStream.WillReadSpan += (s, e) =>
        {
            willReadInvoked = true;
            Assert.False(didReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(6, e.Length);
        };
        this.monitoringStream.DidReadSpan += (s, e) =>
        {
            didReadInvoked = true;
            Assert.True(willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(6, e.Length);
        };
        this.monitoringStream.DidRead += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidReadMemory += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidReadByte += (s, e) => Assert.Fail("Unexpected event.");
        int bytesRead = this.monitoringStream.Read(this.buffer.AsSpan(2, 6));
        Assert.Equal(5, bytesRead);
        Assert.Equal(bytesRead, this.underlyingStream.Position);
        Assert.True(willReadInvoked);
        Assert.True(didReadInvoked);
    }

    [Fact]
    public void Read_Span_RaisesEndOfStream()
    {
        bool wasReadEndOfStreamInvoked = false;
        this.monitoringStream.EndOfStream += (s, e) =>
        {
            Assert.Same(this.monitoringStream, s);
            wasReadEndOfStreamInvoked = true;
        };

        int bytesRead = this.monitoringStream.Read(this.buffer);
        Assert.False(wasReadEndOfStreamInvoked);
        this.monitoringStream.Read(this.buffer);
        Assert.True(wasReadEndOfStreamInvoked);
    }

    [Fact]
    public async Task ReadAsync_Memory_RaisesEvents()
    {
        bool willReadInvoked = false;
        bool didReadInvoked = false;
        this.monitoringStream.WillReadMemory += (s, e) =>
        {
            willReadInvoked = true;
            Assert.False(didReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(6, e.Length);
        };
        this.monitoringStream.DidReadMemory += (s, e) =>
        {
            didReadInvoked = true;
            Assert.True(willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(6, e.Length);
        };
        this.monitoringStream.DidRead += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidReadSpan += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidReadByte += (s, e) => Assert.Fail("Unexpected event.");
        int bytesRead = await this.monitoringStream.ReadAsync(this.buffer.AsMemory(2, 6));
        Assert.Equal(5, bytesRead);
        Assert.Equal(bytesRead, this.underlyingStream.Position);
        Assert.True(willReadInvoked);
        Assert.True(didReadInvoked);
    }

    [Fact]
    public async Task ReadAsync_Memory_RaisesEndOfStream()
    {
        bool wasReadEndOfStreamInvoked = false;
        this.monitoringStream.EndOfStream += (s, e) =>
        {
            Assert.Same(this.monitoringStream, s);
            wasReadEndOfStreamInvoked = true;
        };

        int bytesRead = await this.monitoringStream.ReadAsync(this.buffer);
        Assert.False(wasReadEndOfStreamInvoked);
        await this.monitoringStream.ReadAsync(this.buffer);
        Assert.True(wasReadEndOfStreamInvoked);
    }

    [Fact]
    public void Write_Span_RaisesEvents()
    {
        bool willWriteInvoked = false;
        bool didWriteInvoked = false;
        this.monitoringStream.WillWriteSpan += (s, e) =>
        {
            willWriteInvoked = true;
            Assert.False(didWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(3, e.Length);
        };
        this.monitoringStream.DidWriteSpan += (s, e) =>
        {
            didWriteInvoked = true;
            Assert.True(willWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(3, e.Length);
        };
        this.monitoringStream.DidWrite += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidWriteMemory += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidWriteByte += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.Write(this.buffer.AsSpan(2, 3));
        Assert.True(willWriteInvoked);
        Assert.True(didWriteInvoked);
        Assert.Equal(new byte[] { 8, 9, 10, 4, 5 }, this.underlyingStream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_Memory_RaisesEvents()
    {
        int willWriteInvoked = 0;
        int didWriteInvoked = 0;
        this.monitoringStream.WillWriteMemory += (s, e) =>
        {
            Assert.Equal(0, willWriteInvoked);
            willWriteInvoked++;
            Assert.Equal(0, didWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(3, e.Length);
        };
        this.monitoringStream.DidWriteMemory += (s, e) =>
        {
            Assert.Equal(0, didWriteInvoked);
            Assert.Equal(1, willWriteInvoked);
            didWriteInvoked++;
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(3, e.Length);
        };
        this.monitoringStream.DidWrite += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidWriteSpan += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidWriteByte += (s, e) => Assert.Fail("Unexpected event.");
        await this.monitoringStream.WriteAsync(this.buffer.AsMemory(2, 3));
        Assert.Equal(1, willWriteInvoked);
        Assert.Equal(1, didWriteInvoked);
        Assert.Equal(new byte[] { 8, 9, 10, 4, 5 }, this.underlyingStream.ToArray());
    }

#endif
}
