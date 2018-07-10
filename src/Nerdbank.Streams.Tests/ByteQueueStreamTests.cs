// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Validation;
using Xunit;
using Xunit.Abstractions;

public class ByteQueueStreamTests : TestBase
{
    private const int InitialBufferSize = 16;

    private const int MaxBufferSize = 40;

    private readonly Random random = new Random();

    private ByteQueueStream stream = new ByteQueueStream(InitialBufferSize, MaxBufferSize);

    public ByteQueueStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void CanSeek() => Assert.False(this.stream.CanSeek);

    [Fact]
    public void Length()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Length);
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Length);
    }

    [Fact]
    public void Position()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Position);
        Assert.Throws<NotSupportedException>(() => this.stream.Position = 0);
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Position);
        Assert.Throws<ObjectDisposedException>(() => this.stream.Position = 0);
    }

    [Fact]
    public void IsDisposed()
    {
        Assert.False(this.stream.IsDisposed);
        this.stream.Dispose();
        Assert.True(this.stream.IsDisposed);
    }

    [Fact]
    public void SetLength()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.SetLength(0));
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.SetLength(0));
    }

    [Fact]
    public void Seek()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Seek(0, SeekOrigin.Begin));
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Seek(0, SeekOrigin.Begin));
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
        Assert.True(this.stream.CanWrite);
        this.stream.Dispose();
        Assert.False(this.stream.CanWrite);
    }

    [Fact]
    public void Flush()
    {
        this.stream.Flush();
        Assert.True(this.stream.FlushAsync().IsCompleted);
    }

    [Fact]
    public async Task WriteThenRead()
    {
        byte[] sendBuffer = this.GetRandomBuffer();
        await this.stream.WriteAsync(sendBuffer, 0, sendBuffer.Length).WithCancellation(this.TimeoutToken);
        byte[] recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(this.stream, recvBuffer);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Fact]
    public void Write_InputValidation()
    {
        Assert.Throws<ArgumentNullException>(() => this.stream.Write(null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Write(new byte[MaxBufferSize + 1], 0, MaxBufferSize + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Write(new byte[5], 0, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Write(new byte[5], 5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Write(new byte[5], 3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Write(new byte[5], -1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Write(new byte[5], 2, -1));

        this.stream.Write(new byte[5], 5, 0);
    }

    [Fact]
    public void Write_ThrowsObjectDisposedException()
    {
        this.stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream.Write(new byte[1], 0, 1));
    }

    [Theory]
    [PairwiseData]
    public async Task WriteManyThenRead([CombinatorialValues(InitialBufferSize / 2, InitialBufferSize - 1, InitialBufferSize, InitialBufferSize + 1, MaxBufferSize - 1, MaxBufferSize)] int bytes, [CombinatorialValues(1, 2, 3)] int steps)
    {
        int typicalWriteSize = bytes / steps;
        byte[] sendBuffer = this.GetRandomBuffer(bytes);
        int bytesWritten = 0;
        for (int i = 0; i < steps; i++)
        {
            await this.stream.WriteAsync(sendBuffer, bytesWritten, typicalWriteSize).WithCancellation(this.TimeoutToken);
            bytesWritten += typicalWriteSize;
        }

        // Write the balance of the bytes
        await this.stream.WriteAsync(sendBuffer, bytesWritten, bytes - bytesWritten).WithCancellation(this.TimeoutToken);

        byte[] recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(this.stream, recvBuffer);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(2.1)]
    [InlineData(2.9)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task WriteWriteRead_Loop_WriteRead(float stepsPerBuffer)
    {
        const int maxBufferMultiplier = 3;
        float steps = stepsPerBuffer * maxBufferMultiplier;
        byte[] sendBuffer = this.GetRandomBuffer(MaxBufferSize * maxBufferMultiplier);
        byte[] recvBuffer = new byte[sendBuffer.Length];
        int typicalWriteSize = (int)(sendBuffer.Length / steps);
        Assumes.True(typicalWriteSize * 2 <= MaxBufferSize, "We need to be able to write twice in a row.");
        int bytesWritten = 0;
        int bytesRead = 0;
        for (int i = 0; i < Math.Floor(steps); i++)
        {
            await this.stream.WriteAsync(sendBuffer, bytesWritten, typicalWriteSize).WithCancellation(this.TimeoutToken);
            bytesWritten += typicalWriteSize;

            if (i > 0)
            {
                await this.ReadAsync(this.stream, recvBuffer, typicalWriteSize, bytesRead);
                bytesRead += typicalWriteSize;
                Assert.Equal(sendBuffer.Take(bytesRead), recvBuffer.Take(bytesRead));
            }
        }

        // Write the balance of the bytes
        await this.stream.WriteAsync(sendBuffer, bytesWritten, sendBuffer.Length - bytesWritten).WithCancellation(this.TimeoutToken);

        // Read the balance
        await this.ReadAsync(this.stream, recvBuffer, recvBuffer.Length - bytesRead, bytesRead);

        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Fact(Skip = "Deadlocks till we implement async I/O methods")]
    public async Task ReadThenWrite()
    {
        byte[] sendBuffer = this.GetRandomBuffer();
        byte[] recvBuffer = new byte[sendBuffer.Length];
        Task readTask = this.ReadAsync(this.stream, recvBuffer);
        await this.stream.WriteAsync(sendBuffer, 0, sendBuffer.Length).WithCancellation(this.TimeoutToken);
        await readTask.WithCancellation(this.TimeoutToken);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    private byte[] GetRandomBuffer(int size = 20)
    {
        byte[] buffer = new byte[size];
        this.random.NextBytes(buffer);
        return buffer;
    }
}
