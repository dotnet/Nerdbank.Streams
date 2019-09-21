// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using Moq;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class SubstreamTests : TestBase
{
    internal const int DefaultBufferSize = 4096;
    private MemoryStream underlyingStream = new MemoryStream();

    public SubstreamTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void WriteSubstream_Null()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.WriteSubstream(null!));
    }

    [Fact]
    public void ReadSubstream_Null()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.ReadSubstream(null!));
    }

    [Fact]
    public void CanRead()
    {
        Assert.True(this.underlyingStream.ReadSubstream().CanRead);
        Assert.False(this.underlyingStream.WriteSubstream().CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.False(this.underlyingStream.ReadSubstream().CanWrite);
        Assert.True(this.underlyingStream.WriteSubstream().CanWrite);
    }

    [Fact]
    public void CanSeek()
    {
        Assert.False(this.underlyingStream.ReadSubstream().CanSeek);
        Assert.False(this.underlyingStream.WriteSubstream().CanSeek);
    }

    [Fact]
    public void ReadSubstream_Position()
    {
        var substream = this.underlyingStream.ReadSubstream();
        Assert.Throws<NotSupportedException>(() => substream.Position = 0);
        Assert.Throws<NotSupportedException>(() => substream.Position);
        substream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => substream.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => substream.Position);
    }

    [Theory]
    [PairwiseData]
    public async Task WriteSubstream_Position(bool async)
    {
        var substream = this.underlyingStream.WriteSubstream();
        Assert.Throws<NotSupportedException>(() => substream.Position = 0);
        Assert.Throws<NotSupportedException>(() => substream.Position);
        await this.DisposeSyncOrAsync(substream, async);
        Assert.Throws<ObjectDisposedException>(() => substream.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => substream.Position);
    }

    [Fact]
    public void ReadSubstream_Length()
    {
        var substream = this.underlyingStream.ReadSubstream();
        Assert.Throws<NotSupportedException>(() => substream.Length);
        substream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => substream.Length);
    }

    [Theory]
    [PairwiseData]
    public async Task WriteSubstream_Length(bool async)
    {
        var substream = this.underlyingStream.WriteSubstream();
        Assert.Throws<NotSupportedException>(() => substream.Length);
        await this.DisposeSyncOrAsync(substream, async);
        Assert.Throws<ObjectDisposedException>(() => substream.Length);
    }

    [Theory]
    [PairwiseData]
    public async Task WriteSubstream_Seek(bool async)
    {
        var substream = this.underlyingStream.WriteSubstream();
        Assert.Throws<NotSupportedException>(() => substream.Seek(0, SeekOrigin.Begin));
        await this.DisposeSyncOrAsync(substream, async);
        Assert.Throws<ObjectDisposedException>(() => substream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void ReadSubstream_Seek()
    {
        var substream = this.underlyingStream.ReadSubstream();
        Assert.Throws<NotSupportedException>(() => substream.Seek(0, SeekOrigin.Begin));
        substream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => substream.Seek(0, SeekOrigin.Begin));
    }

    [Theory]
    [PairwiseData]
    public async Task WriteSubstream_SetLength(bool async)
    {
        var substream = this.underlyingStream.WriteSubstream();
        Assert.Throws<NotSupportedException>(() => substream.SetLength(0));
        await this.DisposeSyncOrAsync(substream, async);
        Assert.Throws<ObjectDisposedException>(() => substream.SetLength(0));
    }

    [Fact]
    public void ReadSubstream_SetLength()
    {
        var substream = this.underlyingStream.ReadSubstream();
        Assert.Throws<NotSupportedException>(() => substream.SetLength(0));
        substream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => substream.SetLength(0));
    }

    [Fact]
    public void CanTimeout()
    {
        Assert.False(this.underlyingStream.ReadSubstream().CanTimeout);
        Assert.False(this.underlyingStream.WriteSubstream().CanTimeout);

        var mockStream = new Mock<Stream>();
        mockStream.SetupGet(s => s.CanRead).Returns(true);
        mockStream.SetupGet(s => s.CanWrite).Returns(true);
        mockStream.SetupGet(s => s.CanTimeout).Returns(true);

        Assert.True(mockStream.Object.ReadSubstream().CanTimeout);
        Assert.True(mockStream.Object.WriteSubstream().CanTimeout);
    }

    [Theory]
    [PairwiseData]
    public async Task ReadSubstream_Write(bool async)
    {
        var substream = this.underlyingStream.ReadSubstream();
        await Assert.ThrowsAsync<NotSupportedException>(() => this.WriteSyncOrAsync(substream, async, new byte[1], 0, 1));
        substream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.WriteSyncOrAsync(substream, async, new byte[1], 0, 1));
    }

    [Theory]
    [PairwiseData]
    public async Task ReadSubstream_Flush(bool async)
    {
        var substream = this.underlyingStream.ReadSubstream();
        await Assert.ThrowsAsync<NotSupportedException>(() => this.FlushSyncOrAsync(substream, async));
        substream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.FlushSyncOrAsync(substream, async));
    }

    [Theory]
    [PairwiseData]
    public async Task WriteSubstream_Read(bool async)
    {
        var substream = this.underlyingStream.WriteSubstream();
        await Assert.ThrowsAsync<NotSupportedException>(() => this.ReadSyncOrAsync(substream, async, new byte[1], 0, 1));
        await this.DisposeSyncOrAsync(substream, async);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.ReadSyncOrAsync(substream, async, new byte[1], 0, 1));
    }

    [Theory]
    [PairwiseData]
    public async Task Write_Read([CombinatorialValues(0, 1, 3, 8 * 1024)] int substreamLength, bool async)
    {
        var substream = this.underlyingStream.WriteSubstream();
        var substreamBuffer = this.GetRandomBuffer(substreamLength);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 0, substreamLength);
        await this.DisposeSyncOrAsync(substream, async);

        this.underlyingStream.Write(new byte[] { 0xaa }, 0, 1);
        this.underlyingStream.Position = 0;

        var readSubstream = this.underlyingStream.ReadSubstream();
        var readBuffer = new byte[substreamLength + 5];
        int bytesRead = await this.ReadSyncOrAsync(readSubstream, async, readBuffer, 0, readBuffer.Length);
        Assert.Equal(substreamLength, bytesRead);
        Assert.Equal(substreamBuffer, readBuffer.Take(substreamLength));

        Assert.Equal(0, await this.ReadSyncOrAsync(readSubstream, async, readBuffer, 0, readBuffer.Length));
        Assert.Equal(0, await this.ReadSyncOrAsync(readSubstream, async, readBuffer, 0, readBuffer.Length));

        bytesRead = await this.underlyingStream.ReadAsync(readBuffer, 0, 2);
        Assert.Equal(1, bytesRead);
        Assert.Equal(0xaa, readBuffer[0]);
    }

    [Theory]
    [PairwiseData]
    public async Task WriteInManySmallChunks_ReadInLargeOnes(bool async)
    {
        int bufferSize = 64;
        var substream = this.underlyingStream.WriteSubstream(bufferSize);
        var substreamBuffer = this.GetRandomBuffer(256);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 0, 5);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 5, 15);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 20, 20);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 40, 128);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 168, substreamBuffer.Length - 168);
        await this.DisposeSyncOrAsync(substream, async);

        this.underlyingStream.Position = 0;
        var readSubstream = this.underlyingStream.ReadSubstream();
        var readBuffer = new byte[substreamBuffer.Length + 5];
        int bytesRead = await this.ReadSyncOrAsync(readSubstream, async, readBuffer, 0, readBuffer.Length);
        this.Logger.WriteLine($"Block of {bytesRead} bytes just read.");
        Assert.True(bytesRead >= bufferSize); // confirm we get more than just the tiny sizes we wrote out
        while (bytesRead < substreamBuffer.Length)
        {
            int bytesJustRead = await this.ReadSyncOrAsync(readSubstream, async, readBuffer, bytesRead, readBuffer.Length - bytesRead);
            this.Logger.WriteLine($"Block of {bytesJustRead} bytes just read.");
            Assert.NotEqual(0, bytesJustRead);
            bytesRead += bytesJustRead;
        }

        Assert.Equal(substreamBuffer, readBuffer.Take(substreamBuffer.Length));
    }

    [Theory]
    [PairwiseData]
    public async Task Flush_WritesOutSmallBuffers(bool async)
    {
        int bufferSize = 64;
        var substream = this.underlyingStream.WriteSubstream(bufferSize);
        var substreamBuffer = this.GetRandomBuffer(256);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 0, 5);
        await this.FlushSyncOrAsync(substream, async);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 5, 15);
        await this.FlushSyncOrAsync(substream, async);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 20, 20);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 40, 128);
        await this.WriteSyncOrAsync(substream, async, substreamBuffer, 168, substreamBuffer.Length - 168);
        await this.DisposeSyncOrAsync(substream, async);

        this.underlyingStream.Position = 0;
        var readSubstream = this.underlyingStream.ReadSubstream();
        var readBuffer = new byte[substreamBuffer.Length + 5];
        int bytesRead = 0;
        int bytesJustRead = await this.ReadSyncOrAsync(readSubstream, async, readBuffer, bytesRead, readBuffer.Length);
        bytesRead += bytesJustRead;
        this.Logger.WriteLine($"Block of {bytesJustRead} bytes just read.");
        Assert.Equal(5, bytesJustRead);
        bytesJustRead = await this.ReadSyncOrAsync(readSubstream, async, readBuffer, bytesRead, readBuffer.Length);
        bytesRead += bytesJustRead;
        this.Logger.WriteLine($"Block of {bytesJustRead} bytes just read.");
        Assert.Equal(15, bytesJustRead);
        while (bytesRead < substreamBuffer.Length)
        {
            bytesJustRead = await this.ReadSyncOrAsync(readSubstream, async, readBuffer, bytesRead, readBuffer.Length - bytesRead);
            this.Logger.WriteLine($"Block of {bytesJustRead} bytes just read.");
            Assert.NotEqual(0, bytesJustRead);
            bytesRead += bytesJustRead;
        }

        Assert.Equal(substreamBuffer, readBuffer.Take(substreamBuffer.Length));
    }

    [Theory]
    [PairwiseData]
    public async Task Flush_RepeatedlyDoesNotWriteMore(bool async)
    {
        var substream = this.underlyingStream.WriteSubstream();
        await this.FlushSyncOrAsync(substream, async);
        await this.FlushSyncOrAsync(substream, async);
        Assert.Equal(0, this.underlyingStream.Length);
    }

    [Theory]
    [PairwiseData]
    public async Task Write_AfterDisposeThrows(bool async)
    {
        var substream = this.underlyingStream.WriteSubstream();
        await this.DisposeSyncOrAsync(substream, async);
        Assert.False(substream.CanWrite);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.WriteSyncOrAsync(substream, async, new byte[1], 0, 1));
    }

    private async Task FlushSyncOrAsync(Stream stream, bool async)
    {
        if (async)
        {
            await stream.FlushAsync();
        }
        else
        {
            stream.Flush();
        }
    }

    private async Task<int> ReadSyncOrAsync(Stream stream, bool async, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (async)
        {
            return await stream.ReadAsync(buffer, offset, count, cancellationToken);
        }
        else
        {
            return stream.Read(buffer, offset, count);
        }
    }

    private async Task WriteSyncOrAsync(Stream stream, bool async, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (async)
        {
            await stream.WriteAsync(buffer, offset, count, cancellationToken);
        }
        else
        {
            stream.Write(buffer, offset, count);
        }
    }

    private async Task DisposeSyncOrAsync(Substream stream, bool async)
    {
        if (async)
        {
            await stream.DisposeAsync();
        }
        else
        {
            stream.Dispose();
        }
    }
}
