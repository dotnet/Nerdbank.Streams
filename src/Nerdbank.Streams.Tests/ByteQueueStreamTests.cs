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

    [Theory]
    [PairwiseData]
    public async Task WriteThenRead(bool useAsync)
    {
        byte[] sendBuffer = this.GetRandomBuffer();
        await this.WriteAsync(sendBuffer, 0, sendBuffer.Length, useAsync);
        byte[] recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(this.stream, recvBuffer, isAsync: useAsync);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Theory]
    [PairwiseData]
    public async Task Write_InputValidation(bool useAsync)
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => this.WriteAsync(null, 0, 0, isAsync: useAsync));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.WriteAsync(new byte[MaxBufferSize + 1], 0, MaxBufferSize + 1, isAsync: useAsync));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.WriteAsync(new byte[5], 0, 6, isAsync: useAsync));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.WriteAsync(new byte[5], 5, 1, isAsync: useAsync));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.WriteAsync(new byte[5], 3, 3, isAsync: useAsync));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.WriteAsync(new byte[5], -1, 2, isAsync: useAsync));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.WriteAsync(new byte[5], 2, -1, isAsync: useAsync));

        await this.WriteAsync(new byte[5], 5, 0, useAsync);
    }

    [Theory]
    [PairwiseData]
    public async Task Write_ThrowsObjectDisposedException(bool useAsync)
    {
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.WriteAsync(new byte[1], 0, 1, useAsync));
    }

    [Theory]
    [CombinatorialData]
    public async Task WriteManyThenRead([CombinatorialValues(InitialBufferSize / 2, InitialBufferSize - 1, InitialBufferSize, InitialBufferSize + 1, MaxBufferSize - 1, MaxBufferSize)] int bytes, [CombinatorialValues(1, 2, 3)] int steps, bool useAsync)
    {
        int typicalWriteSize = bytes / steps;
        byte[] sendBuffer = this.GetRandomBuffer(bytes);
        int bytesWritten = 0;
        for (int i = 0; i < steps; i++)
        {
            await this.WriteAsync(sendBuffer, bytesWritten, typicalWriteSize, useAsync);
            bytesWritten += typicalWriteSize;
        }

        // Write the balance of the bytes
        await this.WriteAsync(sendBuffer, bytesWritten, bytes - bytesWritten, useAsync);

        byte[] recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(this.stream, recvBuffer, isAsync: useAsync);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Theory]
    [CombinatorialData]
    public async Task WriteWriteRead_Loop_WriteRead([CombinatorialValues(2, 2.1, 2.9, 3, 4, 5, 6)] float stepsPerBuffer, bool useAsync)
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
            await this.WriteAsync(sendBuffer, bytesWritten, typicalWriteSize, useAsync);
            bytesWritten += typicalWriteSize;

            if (i > 0)
            {
                await this.ReadAsync(this.stream, recvBuffer, typicalWriteSize, bytesRead, useAsync);
                bytesRead += typicalWriteSize;
                Assert.Equal(sendBuffer.Take(bytesRead), recvBuffer.Take(bytesRead));
            }
        }

        // Write the balance of the bytes
        await this.WriteAsync(sendBuffer, bytesWritten, sendBuffer.Length - bytesWritten, useAsync);

        // Read the balance
        await this.ReadAsync(this.stream, recvBuffer, recvBuffer.Length - bytesRead, bytesRead, useAsync);

        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Fact]
    public async Task ReadAsyncThenWriteAsync()
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

    private async Task WriteAsync(byte[] buffer, int offset, int count, bool isAsync)
    {
        if (isAsync)
        {
            await this.stream.WriteAsync(buffer, offset, count, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        }
        else
        {
            this.stream.Write(buffer, offset, count);
        }
    }
}
