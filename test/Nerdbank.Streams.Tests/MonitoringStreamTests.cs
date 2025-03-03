// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable CA2022 // Observe return values from Stream.Read calls

using Nerdbank.Streams;
using NSubstitute;
using Xunit;

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
        int willReadInvoked = 0;
        int didReadInvoked = 0;
        int willReadAnyInvoked = 0;
        int didReadAnyInvoked = 0;
        this.monitoringStream.WillRead += (s, e) =>
        {
            willReadInvoked++;
            Assert.Equal(0, didReadInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 6), e);
        };
        this.monitoringStream.WillReadAny += (s, e) =>
        {
            willReadAnyInvoked++;
            Assert.Equal(0, didReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 6), e);
        };
#pragma warning disable CS0618 // Testing an obsolete API
        this.monitoringStream.DidRead += (s, e) =>
#pragma warning restore CS0618 // Testing an obsolete API
        {
            didReadInvoked++;
            Assert.Equal(1, willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
        this.monitoringStream.DidReadAny += (s, e) =>
        {
            didReadAnyInvoked++;
            Assert.Equal(1, willReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
        int bytesRead = this.monitoringStream.Read(this.buffer, 2, 6);
        Assert.Equal(5, bytesRead);
        Assert.Equal(bytesRead, this.underlyingStream.Position);
        Assert.Equal(1, willReadInvoked);
        Assert.Equal(1, didReadInvoked);
        Assert.Equal(1, willReadAnyInvoked);
        Assert.Equal(1, didReadAnyInvoked);
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
        int willReadAnyInvoked = 0;
        int didReadAnyInvoked = 0;
        this.monitoringStream.WillRead += (s, e) =>
        {
            willReadInvoked = true;
            Assert.False(didReadInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 6), e.AsSpan());
        };
#pragma warning disable CS0618 // Testing an obsolete API
        this.monitoringStream.DidRead += (s, e) =>
#pragma warning restore CS0618 // Testing an obsolete API
        {
            didReadInvoked = true;
            Assert.True(willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
        this.monitoringStream.WillReadAny += (s, e) =>
        {
            willReadAnyInvoked++;
            Assert.Equal(0, didReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 6), e);
        };
        this.monitoringStream.DidReadAny += (s, e) =>
        {
            didReadAnyInvoked++;
            Assert.Equal(1, willReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
        int bytesRead = await this.monitoringStream.ReadAsync(this.buffer, 2, 6);
        Assert.Equal(5, bytesRead);
        Assert.Equal(bytesRead, this.underlyingStream.Position);
        Assert.True(willReadInvoked);
        Assert.True(didReadInvoked);
        Assert.Equal(1, willReadAnyInvoked);
        Assert.Equal(1, didReadAnyInvoked);
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
        int willReadAnyInvoked = 0;
        int didReadAnyInvoked = 0;
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
        this.monitoringStream.WillReadAny += (s, e) =>
        {
            willReadAnyInvoked++;
            Assert.Equal(0, didReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(1, e.Length);
        };
        this.monitoringStream.DidReadAny += (s, e) =>
        {
            didReadAnyInvoked++;
            Assert.Equal(1, willReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan([1], e);
        };
        int byteRead = this.monitoringStream.ReadByte();
        Assert.Equal(1, byteRead);
        Assert.Equal(1, this.underlyingStream.Position);
        Assert.True(willReadInvoked);
        Assert.True(didReadInvoked);
        Assert.Equal(1, willReadAnyInvoked);
        Assert.Equal(1, didReadAnyInvoked);
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
        int willWriteAnyInvoked = 0;
        int didWriteAnyInvoked = 0;
        this.monitoringStream.WillWrite += (s, e) =>
        {
            willWriteInvoked = true;
            Assert.False(didWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
#pragma warning disable CS0618 // Testing an obsolete API
        this.monitoringStream.DidWrite += (s, e) =>
#pragma warning restore CS0618 // Testing an obsolete API
        {
            didWriteInvoked = true;
            Assert.True(willWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.WillWriteAny += (s, e) =>
        {
            willWriteAnyInvoked++;
            Assert.Equal(0, didWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.DidWriteAny += (s, e) =>
        {
            didWriteAnyInvoked++;
            Assert.Equal(1, willWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.Write(this.buffer, 2, 3);
        Assert.True(willWriteInvoked);
        Assert.True(didWriteInvoked);
        Assert.Equal(1, willWriteAnyInvoked);
        Assert.Equal(1, didWriteAnyInvoked);
        Assert.Equal(new byte[] { 8, 9, 10, 4, 5 }, this.underlyingStream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_RaisesEvents()
    {
        bool willWriteInvoked = false;
        bool didWriteInvoked = false;
        int willWriteAnyInvoked = 0;
        int didWriteAnyInvoked = 0;
        this.monitoringStream.WillWrite += (s, e) =>
        {
            willWriteInvoked = true;
            Assert.False(didWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
#pragma warning disable CS0618 // Testing an obsolete API
        this.monitoringStream.DidWrite += (s, e) =>
#pragma warning restore CS0618 // Testing an obsolete API
        {
            didWriteInvoked = true;
            Assert.True(willWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.WillWriteAny += (s, e) =>
        {
            willWriteAnyInvoked++;
            Assert.Equal(0, didWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.DidWriteAny += (s, e) =>
        {
            didWriteAnyInvoked++;
            Assert.Equal(1, willWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        await this.monitoringStream.WriteAsync(this.buffer, 2, 3);
        Assert.True(willWriteInvoked);
        Assert.True(didWriteInvoked);
        Assert.Equal(1, willWriteAnyInvoked);
        Assert.Equal(1, didWriteAnyInvoked);
        Assert.Equal(new byte[] { 8, 9, 10, 4, 5 }, this.underlyingStream.ToArray());
    }

    [Fact]
    public void WriteByte_RaisesEvents()
    {
        bool willWriteInvoked = false;
        bool didWriteInvoked = false;
        int willWriteAnyInvoked = 0;
        int didWriteAnyInvoked = 0;
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
        this.monitoringStream.WillWriteAny += (s, e) =>
        {
            willWriteAnyInvoked++;
            Assert.Equal(0, didWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan([11], e);
        };
        this.monitoringStream.DidWriteAny += (s, e) =>
        {
            didWriteAnyInvoked++;
            Assert.Equal(1, willWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan([11], e);
        };
        this.monitoringStream.WriteByte(11);
        Assert.True(willWriteInvoked);
        Assert.True(didWriteInvoked);
        Assert.Equal(1, willWriteAnyInvoked);
        Assert.Equal(1, didWriteAnyInvoked);
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
        int willReadSpanInvoked = 0;
        int didReadSpanInvoked = 0;
        int didReadInvoked = 0;
        int willReadAnyInvoked = 0;
        int didReadAnyInvoked = 0;
        this.monitoringStream.WillReadSpan += (s, e) =>
        {
            willReadSpanInvoked++;
            Assert.Equal(0, didReadSpanInvoked);
            Assert.Same(this.monitoringStream, s);
            Assert.Equal(6, e.Length);
        };
        this.monitoringStream.DidReadSpan += (s, e) =>
        {
            didReadSpanInvoked++;
            Assert.Equal(1, willReadSpanInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
#pragma warning disable CS0618 // Testing an obsolete API
        this.monitoringStream.DidRead += (s, e) =>
#pragma warning restore CS0618 // Testing an obsolete API
        {
            didReadInvoked++;
            Assert.Equal(1, willReadSpanInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
        this.monitoringStream.WillReadAny += (s, e) =>
        {
            willReadAnyInvoked++;
            Assert.Equal(0, didReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 6), e);
        };
        this.monitoringStream.DidReadAny += (s, e) =>
        {
            didReadAnyInvoked++;
            Assert.Equal(1, willReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
        this.monitoringStream.DidReadMemory += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidReadByte += (s, e) => Assert.Fail("Unexpected event.");
        int bytesRead = this.monitoringStream.Read(this.buffer.AsSpan(2, 6));
        Assert.Equal(5, bytesRead);
        Assert.Equal(bytesRead, this.underlyingStream.Position);
        Assert.Equal(1, willReadSpanInvoked);
        Assert.Equal(1, didReadSpanInvoked);
        Assert.Equal(1, didReadInvoked);
        Assert.Equal(1, willReadAnyInvoked);
        Assert.Equal(1, didReadAnyInvoked);
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
        bool didReadMemoryInvoked = false;
        bool didReadInvoked = false;
        int willReadAnyInvoked = 0;
        int didReadAnyInvoked = 0;
        this.monitoringStream.WillReadMemory += (s, e) =>
        {
            willReadInvoked = true;
            Assert.False(didReadMemoryInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 6), e.Span);
        };
        this.monitoringStream.DidReadMemory += (s, e) =>
        {
            didReadMemoryInvoked = true;
            Assert.True(willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e.Span);
        };
#pragma warning disable CS0618 // Testing an obsolete API
        this.monitoringStream.DidRead += (s, e) =>
#pragma warning restore CS0618 // Testing an obsolete API
        {
            didReadInvoked = true;
            Assert.True(willReadInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
        this.monitoringStream.WillReadAny += (s, e) =>
        {
            willReadAnyInvoked++;
            Assert.Equal(0, didReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 6), e);
        };
        this.monitoringStream.DidReadAny += (s, e) =>
        {
            didReadAnyInvoked++;
            Assert.Equal(1, willReadAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 5), e);
        };
        this.monitoringStream.DidReadSpan += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidReadByte += (s, e) => Assert.Fail("Unexpected event.");
        int bytesRead = await this.monitoringStream.ReadAsync(this.buffer.AsMemory(2, 6));
        Assert.Equal(5, bytesRead);
        Assert.Equal(bytesRead, this.underlyingStream.Position);
        Assert.True(willReadInvoked);
        Assert.True(didReadMemoryInvoked);
        Assert.True(didReadInvoked);
        Assert.Equal(1, willReadAnyInvoked);
        Assert.Equal(1, didReadAnyInvoked);
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
        bool didWriteSpanInvoked = false;
        bool didWriteInvoked = false;
        int willWriteAnyInvoked = 0;
        int didWriteAnyInvoked = 0;
        this.monitoringStream.WillWriteSpan += (s, e) =>
        {
            willWriteInvoked = true;
            Assert.False(didWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.DidWriteSpan += (s, e) =>
        {
            didWriteSpanInvoked = true;
            Assert.True(willWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
#pragma warning disable CS0618 // Testing an obsolete API
        this.monitoringStream.DidWrite += (s, e) =>
#pragma warning restore CS0618 // Testing an obsolete API
        {
            didWriteInvoked = true;
            Assert.True(willWriteInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.WillWriteAny += (s, e) =>
        {
            willWriteAnyInvoked++;
            Assert.Equal(0, didWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.DidWriteAny += (s, e) =>
        {
            didWriteAnyInvoked++;
            Assert.Equal(1, willWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.DidWriteMemory += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidWriteByte += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.Write(this.buffer.AsSpan(2, 3));
        Assert.True(willWriteInvoked);
        Assert.True(didWriteInvoked);
        Assert.True(didWriteSpanInvoked);
        Assert.Equal(1, willWriteAnyInvoked);
        Assert.Equal(1, didWriteAnyInvoked);
        Assert.Equal(new byte[] { 8, 9, 10, 4, 5 }, this.underlyingStream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_Memory_RaisesEvents()
    {
        int willWriteInvoked = 0;
        int didWriteMemoryInvoked = 0;
        int didWriteInvoked = 0;
        int willWriteAnyInvoked = 0;
        int didWriteAnyInvoked = 0;
        this.monitoringStream.WillWriteMemory += (s, e) =>
        {
            Assert.Equal(0, willWriteInvoked);
            willWriteInvoked++;
            Assert.Equal(0, didWriteMemoryInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e.Span);
        };
        this.monitoringStream.DidWriteMemory += (s, e) =>
        {
            Assert.Equal(0, didWriteMemoryInvoked);
            Assert.Equal(1, willWriteInvoked);
            didWriteMemoryInvoked++;
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e.Span);
        };
#pragma warning disable CS0618 // Testing an obsolete API
        this.monitoringStream.DidWrite += (s, e) =>
#pragma warning restore CS0618 // Testing an obsolete API
        {
            Assert.Equal(0, didWriteInvoked);
            Assert.Equal(1, willWriteInvoked);
            didWriteInvoked++;
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.WillWriteAny += (s, e) =>
        {
            willWriteAnyInvoked++;
            Assert.Equal(0, didWriteAnyInvoked);
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.DidWriteAny += (s, e) =>
        {
            Assert.Equal(1, willWriteAnyInvoked);
            didWriteAnyInvoked++;
            Assert.Same(this.monitoringStream, s);
            AssertEqualSpan(this.buffer.AsSpan(2, 3), e);
        };
        this.monitoringStream.DidWriteSpan += (s, e) => Assert.Fail("Unexpected event.");
        this.monitoringStream.DidWriteByte += (s, e) => Assert.Fail("Unexpected event.");
        await this.monitoringStream.WriteAsync(this.buffer.AsMemory(2, 3));
        Assert.Equal(1, willWriteInvoked);
        Assert.Equal(1, didWriteMemoryInvoked);
        Assert.Equal(1, didWriteInvoked);
        Assert.Equal(1, willWriteAnyInvoked);
        Assert.Equal(1, didWriteAnyInvoked);
        Assert.Equal(new byte[] { 8, 9, 10, 4, 5 }, this.underlyingStream.ToArray());
    }

#endif

    private static void AssertEqualSpan(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }
}
