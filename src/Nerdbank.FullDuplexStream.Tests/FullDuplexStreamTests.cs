// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nerdbank;
using Xunit;

public class FullDuplexStreamTests : IDisposable
{
    private static readonly byte[] Data3Bytes = new byte[] { 0x1, 0x3, 0x2 };

    private static readonly byte[] Data5Bytes = new byte[] { 0x1, 0x3, 0x2, 0x5, 0x4 };

    /// <summary>
    /// The time to wait for an async operation to occur within a reasonable time,
    /// when we expect a passing test to wait the entire time.
    /// </summary>
    private static readonly TimeSpan ExpectedAsyncTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private readonly Stream stream1;

    private readonly Stream stream2;

    private readonly CancellationTokenSource testCancellationSource;

    public FullDuplexStreamTests()
    {
        var tuple = FullDuplexStream.CreateStreams();
        Assert.NotNull(tuple);
        Assert.NotNull(tuple.Item1);
        Assert.NotNull(tuple.Item2);

        this.stream1 = tuple.Item1;
        this.stream2 = tuple.Item2;

        TimeSpan timeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TestTimeout;
        this.testCancellationSource = new CancellationTokenSource(timeout);
    }

    protected CancellationToken TestCanceled => this.testCancellationSource.Token;

    /// <summary>
    /// Gets a new <see cref="CancellationToken"/> that will automatically cancel
    /// after <see cref="ExpectedAsyncTimeout"/> has elapsed.
    /// </summary>
    protected CancellationToken ExpectedAsyncTimeoutToken => new CancellationTokenSource(ExpectedAsyncTimeout).Token;

    public void Dispose()
    {
        this.testCancellationSource.Dispose();
    }

    [Fact]
    public void Write_InvalidArgs()
    {
        Assert.Throws<ArgumentNullException>(() => this.stream1.Write(null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[0], -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[0], 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[0], 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[0], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[1], 0, 2));
    }

    [Fact]
    public void Write_CanBeReadOnOtherStream()
    {
        byte[] sentBuffer = Data3Bytes;

        this.stream1.Write(sentBuffer, 0, sentBuffer.Length);
        byte[] buffer = new byte[sentBuffer.Length];
        int bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(sentBuffer.Length, bytesRead);
        Assert.Equal<byte>(sentBuffer, buffer);
    }

    [Fact]
    public void Read_InSmallerBlocks()
    {
        byte[] sentBuffer = Data5Bytes;

        this.stream1.Write(sentBuffer, 0, sentBuffer.Length);
        byte[] buffer = new byte[2];
        int bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, bytesRead);
        Assert.Equal(sentBuffer.Take(2), buffer);

        bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, bytesRead);
        Assert.Equal(sentBuffer.Skip(2).Take(2), buffer);

        bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(1, bytesRead);
        Assert.Equal(sentBuffer.Skip(4), buffer.Take(1));
    }

    [Fact]
    public async Task Read_BeforeWrite()
    {
        byte[] buffer = new byte[5];
        var readTask = this.stream2.ReadAsync(buffer, 0, buffer.Length, this.TestCanceled);
        Assert.False(readTask.IsCompleted);

        this.stream1.Write(Data3Bytes, 0, Data3Bytes.Length);
        int bytesRead = await readTask;
        Assert.Equal(Data3Bytes.Length, bytesRead);
        Assert.Equal(Data3Bytes, buffer.Take(bytesRead));
    }

    [Fact]
    public async Task Read_ReturnsNoBytesWhenRemoteStreamClosed()
    {
        // Verify that closing the transmitting stream after reading has been requested
        // appropriately ends the read attempt.
        byte[] buffer = new byte[5];
        var readTask = this.stream2.ReadAsync(buffer, 0, buffer.Length, this.TestCanceled);
        Assert.False(readTask.IsCompleted);
        this.stream1.Close();
        int bytesRead = await readTask;
        Assert.Equal(0, bytesRead);

        // Verify that reading from a closed stream returns 0 bytes.
        bytesRead = await this.stream2.ReadAsync(buffer, 0, buffer.Length, this.TestCanceled);
        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public async Task Write_EmptyBufferDoesNotTerminateOtherStream()
    {
        this.stream1.Write(Data3Bytes, 0, 0);
        var buffer = new byte[10];
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.stream2.ReadAsync(buffer, 0, buffer.Length, this.ExpectedAsyncTimeoutToken));
    }

    [Fact]
    public void Write_TwiceThenRead()
    {
        this.stream1.Write(Data3Bytes, 0, Data3Bytes.Length);
        this.stream1.Write(Data5Bytes, 0, Data5Bytes.Length);
        byte[] receiveBuffer = new byte[Data3Bytes.Length + Data5Bytes.Length];
        int bytesRead = 0;
        do
        {
            // Per the MSDN documentation, Read can fill less than the provided buffer.
            int bytesJustRead = this.stream2.Read(receiveBuffer, bytesRead, receiveBuffer.Length - bytesRead);
            Assert.NotEqual(0, bytesJustRead);
            bytesRead += bytesJustRead;
        }
        while (bytesRead < receiveBuffer.Length);

        Assert.Equal(Data3Bytes, receiveBuffer.Take(Data3Bytes.Length));
        Assert.Equal(Data5Bytes, receiveBuffer.Skip(Data3Bytes.Length));
    }

    [Fact]
    public void Write_EmptyBuffer()
    {
        this.stream1.Write(new byte[0], 0, 0);
    }

    [Fact]
    public void Write_EmptyTrailingEdgeOfBuffer()
    {
        this.stream1.Write(new byte[1], 1, 0);
    }

    [Fact]
    public void Write_TrailingEdgeOfBuffer()
    {
        this.stream1.Write(new byte[2], 1, 1);
    }

    [Fact]
    public async Task Read_APM()
    {
        byte[] readBuffer = new byte[10];
        Task<int> readTask = Task.Factory.FromAsync(
            (cb, state) => this.stream1.BeginRead(readBuffer, 0, readBuffer.Length, cb, state),
            ar => this.stream1.EndRead(ar),
            null);
        this.stream2.Write(Data3Bytes, 0, Data3Bytes.Length);
        int bytesRead = await readTask;
        Assert.Equal(Data3Bytes.Length, bytesRead);
        Assert.Equal(Data3Bytes, readBuffer.Take(bytesRead));
    }

    [Fact]
    public async Task Write_APM()
    {
        await Task.Factory.FromAsync(
            (cb, state) => this.stream1.BeginWrite(Data3Bytes, 0, Data3Bytes.Length, cb, state),
            ar => this.stream1.EndWrite(ar),
            null);
        byte[] readBuffer = new byte[10];
        int bytesRead = this.stream2.Read(readBuffer, 0, readBuffer.Length);
        Assert.Equal(Data3Bytes.Length, bytesRead);
        Assert.Equal(Data3Bytes, readBuffer.Take(bytesRead));
    }

    [Fact]
    public void Dispose_TwiceDoesNotThrow()
    {
        this.stream1.Dispose();
        this.stream2.Dispose();
        this.stream1.Dispose();
        this.stream2.Dispose();
    }

    [Fact]
    public async Task Dispose_CausesOtherMethodsToThrow()
    {
        this.stream1.Dispose();

        byte[] buffer = new byte[1];
        Assert.Throws<ObjectDisposedException>(() => this.stream1.Write(buffer, 0, 1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream1.WriteAsync(buffer, 0, 1));
        Assert.Throws<ObjectDisposedException>(() => this.stream1.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => this.stream1.Read(buffer, 0, 1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream1.ReadAsync(buffer, 0, 1));
    }

    [Fact]
    public void Dispose_LeadsOtherStreamToEnd()
    {
        this.stream1.Dispose();
        byte[] buffer = new byte[1];
        Assert.Equal(0, this.stream2.Read(buffer, 0, 1));
    }
}
