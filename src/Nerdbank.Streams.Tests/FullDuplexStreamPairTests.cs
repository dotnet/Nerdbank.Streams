// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class FullDuplexStreamPairTests : TestBase
{
    private static readonly byte[] Data3Bytes = new byte[] { 0x1, 0x3, 0x2 };

    private static readonly byte[] Data5Bytes = new byte[] { 0x1, 0x3, 0x2, 0x5, 0x4 };

    private readonly Stream stream1;

    private readonly Stream stream2;

    public FullDuplexStreamPairTests(ITestOutputHelper logger)
        : base(logger)
    {
        var tuple = FullDuplexStream.CreatePair();
        Assert.NotNull(tuple.Item1);
        Assert.NotNull(tuple.Item2);

        this.stream1 = tuple.Item1;
        this.stream2 = tuple.Item2;
    }

    [Fact]
    public void Write_InvalidArgs()
    {
        Assert.Throws<ArgumentNullException>(() => this.stream1.Write(null!, 0, 0));
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
        this.stream1.Flush();
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
        this.stream1.Flush();
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
        var readTask = this.stream2.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.False(readTask.IsCompleted);

        await this.stream1.WriteAsync(Data3Bytes, 0, Data3Bytes.Length).WithCancellation(this.TimeoutToken);
        await this.stream1.FlushAsync(this.TimeoutToken);
        int bytesRead = await readTask.WithCancellation(this.TimeoutToken);
        Assert.Equal(Data3Bytes.Length, bytesRead);
        Assert.Equal(Data3Bytes, buffer.Take(bytesRead));
    }

    [Fact]
    public async Task Read_ReturnsNoBytesWhenRemoteStreamClosed()
    {
        // Verify that closing the transmitting stream after reading has been requested
        // appropriately ends the read attempt.
        byte[] buffer = new byte[5];
        var readTask = this.stream2.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken);
        Assert.False(readTask.IsCompleted);
        this.stream1.Close();
        int bytesRead = await readTask.WithCancellation(this.TimeoutToken);
        Assert.Equal(0, bytesRead);

        // Verify that reading from a closed stream returns 0 bytes.
        bytesRead = await this.stream2.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken);
        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public async Task Write_EmptyBufferDoesNotTerminateOtherStream()
    {
        await this.stream1.WriteAsync(Data3Bytes, 0, 0).WithCancellation(this.TimeoutToken);
        var buffer = new byte[10];
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.stream2.ReadAsync(buffer, 0, buffer.Length, ExpectedTimeoutToken)).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public void Write_TwiceThenRead()
    {
        this.stream1.Write(Data3Bytes, 0, Data3Bytes.Length);
        this.stream1.Write(Data5Bytes, 0, Data5Bytes.Length);
        this.stream1.Flush();
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
        await this.stream2.WriteAsync(Data3Bytes, 0, Data3Bytes.Length).WithCancellation(this.TimeoutToken);
        await this.stream2.FlushAsync(this.TimeoutToken);
        int bytesRead = await readTask.WithCancellation(this.TimeoutToken);
        Assert.Equal(Data3Bytes.Length, bytesRead);
        Assert.Equal(Data3Bytes, readBuffer.Take(bytesRead));
    }

    [Fact]
    public async Task Write_APM()
    {
        await Task.Factory.FromAsync(
            (cb, state) => this.stream1.BeginWrite(Data3Bytes, 0, Data3Bytes.Length, cb, state),
            ar => this.stream1.EndWrite(ar),
            null).WithCancellation(this.TimeoutToken);
        await this.stream1.FlushAsync(this.TimeoutToken);
        byte[] readBuffer = new byte[10];
        int bytesRead = await this.stream2.ReadAsync(readBuffer, 0, readBuffer.Length).WithCancellation(this.TimeoutToken);
        Assert.Equal(Data3Bytes.Length, bytesRead);
        Assert.Equal(Data3Bytes, readBuffer.Take(bytesRead));
    }

    [Fact]
    public void Position_Throws()
    {
        Assert.Throws<NotSupportedException>(() => this.stream1.Position);
        Assert.Throws<NotSupportedException>(() => this.stream1.Position = 0);
        this.stream1.Dispose();

        Assert.Throws<ObjectDisposedException>(() => GetDisposedMemoryStream().Position);
        Assert.Throws<ObjectDisposedException>(() => this.stream1.Position);
        Assert.Throws<ObjectDisposedException>(() => GetDisposedMemoryStream().Position = 0);
        Assert.Throws<ObjectDisposedException>(() => this.stream1.Position = 0);
    }

    [Fact]
    public void Length_ThrowsObjectDisposedException()
    {
        Assert.Throws<NotSupportedException>(() => this.stream1.Length);
        this.stream1.Dispose();

        Assert.Throws<ObjectDisposedException>(() => GetDisposedMemoryStream().Length);
        Assert.Throws<ObjectDisposedException>(() => this.stream1.Length);
    }

    [Fact]
    public void CanSeek()
    {
        Assert.False(this.stream1.CanSeek);
        this.stream1.Dispose();

        Assert.False(GetDisposedMemoryStream().CanSeek);
        Assert.False(this.stream1.CanSeek);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.True(this.stream1.CanWrite);
        this.stream1.Dispose();

        Assert.False(GetDisposedMemoryStream().CanWrite);
        Assert.False(this.stream1.CanWrite);
    }

    [Fact]
    public void CanRead()
    {
        Assert.True(this.stream1.CanRead);
        this.stream1.Dispose();

        Assert.False(GetDisposedMemoryStream().CanRead);
        Assert.False(this.stream1.CanRead);
    }

    [Fact]
    public void Seek()
    {
        Assert.Throws<NotSupportedException>(() => this.stream1.Seek(0, SeekOrigin.Begin));
        this.stream1.Dispose();

        Assert.Throws<ObjectDisposedException>(() => GetDisposedMemoryStream().Seek(0, SeekOrigin.Begin));
        Assert.Throws<ObjectDisposedException>(() => this.stream1.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void SetLength()
    {
        Assert.Throws<NotSupportedException>(() => this.stream1.SetLength(0));
        this.stream1.Dispose();

        Assert.Throws<NotSupportedException>(() => GetDisposedMemoryStream().SetLength(0));
        Assert.Throws<ObjectDisposedException>(() => this.stream1.SetLength(0));
    }

    [Fact]
    public void Read_ThrowsObjectDisposedException()
    {
        this.stream1.Dispose();
        var buffer = new byte[1];
        Assert.Throws<ObjectDisposedException>(() => this.stream1.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task ReadAsync_ThrowsObjectDisposedException()
    {
        this.stream1.Dispose();
        var buffer = new byte[1];
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream1.ReadAsync(buffer, 0, buffer.Length).WithCancellation(this.TimeoutToken));
    }

    [Fact]
    public void Write_ThrowsObjectDisposedException()
    {
        this.stream1.Dispose();
        var buffer = new byte[1];
        Assert.Throws<ObjectDisposedException>(() => this.stream1.Write(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task WriteAsync_ThrowsObjectDisposedException()
    {
        this.stream1.Dispose();
        var buffer = new byte[1];
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream1.WriteAsync(buffer, 0, buffer.Length).WithCancellation(this.TimeoutToken));
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
    public void ReadByte_ThrowsAfterDisposal()
    {
        this.stream1.Dispose();
        Assert.Throws<ObjectDisposedException>(() => this.stream1.ReadByte());
    }

    [Fact]
    public void Dispose_LeadsOtherStreamToEnd()
    {
        this.stream1.Dispose();
        byte[] buffer = new byte[1];
        Assert.Equal(0, this.stream2.Read(buffer, 0, 1));
    }

    [Fact]
    public async Task StreamPairWithUsePipe()
    {
        var pipe1 = this.stream1.UsePipe();
        var pipe2 = this.stream2.UsePipe();

        // Complete the pipe's reader and writer. Assert that this disposes their stream.
        pipe2.Output.Complete();
        pipe2.Input.Complete();

        // Verify that this disposes the stream.
        while (!((IDisposableObservable)this.stream2).IsDisposed && !this.TimeoutToken.IsCancellationRequested)
        {
            await Task.Yield();
        }

        Assert.True(((IDisposableObservable)this.stream2).IsDisposed);

#pragma warning disable CS0618 // Type or member is obsolete

        // Verify that the other end notices.
        await pipe1.Input.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);

        // The other end then decides it is done writing.
        pipe1.Output.Complete();
        await pipe1.Output.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    [Fact]
    public async Task PipePair()
    {
        var (party1, party2) = FullDuplexStream.CreatePipePair();

        // First party indicates they're done sending messages (but might still be reading).
        party1.Output.Complete();

#pragma warning disable CS0618 // Type or member is obsolete

        // Second party recognizes that the other's writing is done, and acknowledges that they're done reading.
        await party2.Input.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
        party2.Input.Complete();
        await party1.Output.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken); // just to show propagation.

        // Second party indicates that they're done writing messages.
        party2.Output.Complete();

        // First party recognizes that the other's writing is done, and acknowledges that they're done reading.
        await party1.Input.WaitForWriterCompletionAsync().WithCancellation(this.TimeoutToken);
        party1.Input.Complete();
        await party2.Output.WaitForReaderCompletionAsync().WithCancellation(this.TimeoutToken); // just to show propagation.
#pragma warning restore RS0030 // Do not used banned APIs
    }

    private static MemoryStream GetDisposedMemoryStream()
    {
        var ms = new MemoryStream();
        ms.Dispose();
        return ms;
    }
}
