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
using Xunit;
using Xunit.Abstractions;

public class HalfDuplexStreamTests : TestBase
{
    private const int ResumeThreshold = 39;

    private const int PauseThreshold = 40;

    private readonly Random random = new Random();

    private HalfDuplexStream stream = new HalfDuplexStream(ResumeThreshold, PauseThreshold);

    public HalfDuplexStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void DefaultCtor()
    {
        var stream = new HalfDuplexStream();
        stream.Dispose();
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
    public async Task WriteManyThenRead([CombinatorialValues(PauseThreshold / 2, PauseThreshold - 1)] int bytes, [CombinatorialValues(1, 2, 3)] int steps, bool useAsync)
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
    public async Task WriteWriteRead_Loop_WriteRead([CombinatorialValues(2, 2.1, 2.9, 3, 4, 5, 6)] float stepsPerBuffer)
    {
        bool useAsync = true;
        const int maxBufferMultiplier = 3;
        float steps = stepsPerBuffer * maxBufferMultiplier;
        byte[] sendBuffer = this.GetRandomBuffer(PauseThreshold * maxBufferMultiplier);
        byte[] recvBuffer = new byte[sendBuffer.Length];
        int typicalWriteSize = (int)(sendBuffer.Length / steps);
        typicalWriteSize = Math.Min(typicalWriteSize, (PauseThreshold / 2) - 1); // We need to be able to write twice in a row.
        int bytesWritten = 0;
        int bytesRead = 0;
        for (int i = 0; i < Math.Floor(steps); i++)
        {
            await this.WriteAsync(sendBuffer, bytesWritten, typicalWriteSize, useAsync);
            bytesWritten += typicalWriteSize;
            await this.stream.FlushAsync();

            if (i > 0)
            {
                await this.ReadAsync(this.stream, recvBuffer, typicalWriteSize, bytesRead, useAsync);
                bytesRead += typicalWriteSize;
                Assert.Equal(sendBuffer.Take(bytesRead), recvBuffer.Take(bytesRead));
            }
        }

        // Write the balance of the bytes
        await this.WriteAsync(sendBuffer, bytesWritten, sendBuffer.Length - bytesWritten, useAsync);
        await this.stream.FlushAsync();

        // Read the balance
        await this.ReadAsync(this.stream, recvBuffer, recvBuffer.Length - bytesRead, bytesRead, useAsync);

        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Theory]
    [CombinatorialData]
    public async Task Write_ThenReadMore(bool useAsync)
    {
        byte[] sendBuffer = new byte[] { 0x1, 0x2 };
        await this.WriteAsync(sendBuffer, 0, sendBuffer.Length, useAsync);
        int bytesRead;
        byte[] recvBuffer = new byte[5];
        if (useAsync)
        {
            bytesRead = await this.stream.ReadAsync(recvBuffer, 0, recvBuffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        }
        else
        {
            bytesRead = this.stream.Read(recvBuffer, 0, recvBuffer.Length);
        }

        Assert.Equal(sendBuffer.Length, bytesRead);
        Assert.Equal(sendBuffer, recvBuffer.Take(bytesRead));
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

    [Theory]
    [CombinatorialData]
    public async Task CompleteWriting(bool useAsync)
    {
        await this.WriteAsync(new byte[3], 0, 3, useAsync);
        this.stream.CompleteWriting();
        byte[] recvbuffer = new byte[5];
        await this.ReadAsync(this.stream, recvbuffer, count: 3, isAsync: useAsync);
        Assert.Equal(0, await this.stream.ReadAsync(recvbuffer, 3, 2, this.TimeoutToken).WithCancellation(this.TimeoutToken));
        Assert.Equal(0, this.stream.Read(recvbuffer, 3, 2));
    }

    protected override void Dispose(bool disposing)
    {
        this.stream.Dispose();
        base.Dispose(disposing);
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
