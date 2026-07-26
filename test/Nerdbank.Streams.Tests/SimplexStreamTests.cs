// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;

public class SimplexStreamTests : TestBase
{
    private const int ResumeThreshold = 39;

    private const int PauseThreshold = 40;

    // Test-local constant for auto-flush threshold. Keep in sync with production value in SimplexStream.
    private const int AutoFlushThreshold = 4096;

    private readonly Random random = new Random();

    private SimplexStream stream = new SimplexStream(ResumeThreshold, PauseThreshold);

    public SimplexStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void DefaultCtor()
    {
        var stream = new SimplexStream();
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
        byte[] sendBuffer = this.GetRandomBuffer(20);
        await this.WriteAsync(sendBuffer, 0, sendBuffer.Length, useAsync);
        await this.stream.FlushAsync(this.TimeoutToken);
        byte[] recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(this.stream, recvBuffer, isAsync: useAsync);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Theory]
    [PairwiseData]
    public async Task Write_InputValidation(bool useAsync)
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => this.WriteAsync(null!, 0, 0, isAsync: useAsync));
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
        await this.stream.FlushAsync(this.TimeoutToken);

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
        await this.stream.FlushAsync(this.TimeoutToken);
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
        byte[] sendBuffer = this.GetRandomBuffer(20);
        byte[] recvBuffer = new byte[sendBuffer.Length];
        Task readTask = this.ReadAsync(this.stream, recvBuffer);
        await this.stream.WriteAsync(sendBuffer, 0, sendBuffer.Length).WithCancellation(this.TimeoutToken);
        await this.stream.FlushAsync(this.TimeoutToken);
        await readTask.WithCancellation(this.TimeoutToken);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Fact]
    public async Task ReadAsync0LengthBufferThenWriteAsync()
    {
        byte[] sendBuffer = this.GetRandomBuffer(20);
        byte[] recvBuffer = Array.Empty<byte>();
        Task readTask = this.ReadAsync(this.stream, recvBuffer);
        await this.stream.WriteAsync(sendBuffer, 0, sendBuffer.Length).WithCancellation(this.TimeoutToken);
        await this.stream.FlushAsync(this.TimeoutToken);
        await readTask.WithCancellation(this.TimeoutToken);

        recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(this.stream, recvBuffer);
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

    [Fact]
    public async Task StreamAsBufferWriter()
    {
        IBufferWriter<byte> writer = this.stream;
        writer.Write(new byte[] { 1, 2, 3 });
        writer.Write(new byte[] { 4, 5, 6, 7, 8, 9 });
        await this.stream.FlushAsync(this.TimeoutToken);
        byte[]? readBuffer = new byte[10];
        int bytesRead = await this.stream.ReadAsync(readBuffer, 0, 10, this.TimeoutToken);
        Assert.Equal(9, bytesRead);
        Assert.Equal(Enumerable.Range(1, 9).Select(i => (byte)i), readBuffer.Take(bytesRead));
    }

    [Fact]
    public void CompleteWriting_ErrorCanBeSetAndIsRethrown()
    {
        this.stream.CompleteWriting(new InvalidOperationException("Test error"));
        Assert.Throws<InvalidOperationException>(() => this.stream.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void CompleteWriting_ErrorIsRethrownAfterAllDataRead()
    {
        byte[] expected = [1, 2, 3];
        this.stream.Write(expected);
        this.stream.CompleteWriting(new InvalidOperationException("Test error"));
        byte[] buffer = new byte[10];
        int read = this.stream.Read(buffer, 0, 4);
        byte[] actual = [.. buffer.Take(read)];
        Assert.Equal(expected, actual);
        Assert.Throws<InvalidOperationException>(() => this.stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void CompleteWriting_ErrorPreservesStackTrace()
    {
        InternalMethodSettingErrorWithCallStack();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => this.stream.Read(new byte[1], 0, 1));

        Assert.Equal("Test error", ex.Message);
        Assert.NotNull(ex.StackTrace);
        Assert.Contains(nameof(InternalMethodSettingErrorWithCallStack), ex.StackTrace, StringComparison.Ordinal);

        [MethodImpl(MethodImplOptions.NoInlining)]
        void InternalMethodSettingErrorWithCallStack()
        {
            try
            {
                throw new InvalidOperationException("Test error");
            }
            catch (Exception ex)
            {
                this.stream.CompleteWriting(ex);
            }
        }
    }

    [Fact]
    public void CompleteWriting_NullError()
    {
        this.stream.CompleteWriting(null);
        byte[] buffer = new byte[10];
        Assert.Equal(0, this.stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void CompleteWriting_FirstErrorIsCaptured()
    {
        this.stream.CompleteWriting(new InvalidOperationException("Test error"));
        this.stream.CompleteWriting(new IOException());
        byte[] buffer = new byte[10];
        Assert.Throws<InvalidOperationException>(() => this.stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void CompleteWriting_SubmitErrorThenCompleteNormally()
    {
        this.stream.CompleteWriting(new InvalidOperationException("Test error"));
        this.stream.CompleteWriting();
        byte[] buffer = new byte[10];
        Assert.Throws<InvalidOperationException>(() => this.stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void CompleteWriting_CompleteSuccessfullyThenWithError()
    {
        this.stream.CompleteWriting();
        this.stream.CompleteWriting(new InvalidOperationException("Test error"));
        byte[] buffer = new byte[10];
        Assert.Equal(0, this.stream.Read(buffer, 0, buffer.Length));
    }

    [Theory]
    [CombinatorialData]
    public async Task AutoFlush_OccursAfter4KB(bool useAsync)
    {
        // Use a stream with larger thresholds to avoid blocking
        using var largeStream = new SimplexStream(8192, 16384);

        // Write exactly 4KB (4096 bytes) which should trigger auto-flush
        byte[] sendBuffer = this.GetRandomBuffer(AutoFlushThreshold);
        if (useAsync)
        {
            await largeStream.WriteAsync(sendBuffer, 0, sendBuffer.Length, this.TimeoutToken);
        }
        else
        {
            largeStream.Write(sendBuffer, 0, sendBuffer.Length);
        }

        // Data should be available for reading without explicit flush
        byte[] recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(largeStream, recvBuffer, isAsync: useAsync);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Theory]
    [CombinatorialData]
    public async Task AutoFlush_DoesNotOccurBelow4KB(bool useAsync)
    {
        // Use a stream with larger thresholds
        using var largeStream = new SimplexStream(8192, 16384);

        // Write less than 4KB
        byte[] sendBuffer = this.GetRandomBuffer(4095);
        if (useAsync)
        {
            await largeStream.WriteAsync(sendBuffer, 0, sendBuffer.Length, this.TimeoutToken);
        }
        else
        {
            largeStream.Write(sendBuffer, 0, sendBuffer.Length);
        }

        // Data should NOT be available without explicit flush - read should timeout
        byte[] recvBuffer = new byte[1];
        Task<int> readTask = largeStream.ReadAsync(recvBuffer, 0, 1, ExpectedTimeoutToken);

        // This should timeout because data hasn't been flushed yet
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await readTask);

        // After explicit flush, data should be available
        await largeStream.FlushAsync();
        recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(largeStream, recvBuffer, isAsync: useAsync);
        Assert.Equal(sendBuffer, recvBuffer);
    }

    [Theory]
    [CombinatorialData]
    public async Task AutoFlush_AccumulatesAcrossMultipleWrites(bool useAsync)
    {
        // Use a stream with larger thresholds to avoid blocking
        using var largeStream = new SimplexStream(8192, 16384);

        // Write 2KB three times (total 6KB) - should auto-flush after second write
        byte[] sendBuffer = this.GetRandomBuffer(6144);

        // First write (2KB) - no flush yet
        if (useAsync)
        {
            await largeStream.WriteAsync(sendBuffer, 0, 2048, this.TimeoutToken);
        }
        else
        {
            largeStream.Write(sendBuffer, 0, 2048);
        }

        // Second write (2KB, total 4KB) - should auto-flush
        if (useAsync)
        {
            await largeStream.WriteAsync(sendBuffer, 2048, 2048, this.TimeoutToken);
        }
        else
        {
            largeStream.Write(sendBuffer, 2048, 2048);
        }

        // Data should be available for reading (4KB)
        byte[] recvBuffer = new byte[AutoFlushThreshold];
        await this.ReadAsync(largeStream, recvBuffer, isAsync: useAsync);
        Assert.Equal(sendBuffer.Take(AutoFlushThreshold), recvBuffer);

        // Third write (2KB) - not flushed yet
        if (useAsync)
        {
            await largeStream.WriteAsync(sendBuffer, AutoFlushThreshold, 2048, this.TimeoutToken);
        }
        else
        {
            largeStream.Write(sendBuffer, AutoFlushThreshold, 2048);
        }

        // Explicitly flush to make remaining data available
        await largeStream.FlushAsync();
        recvBuffer = new byte[2048];
        await this.ReadAsync(largeStream, recvBuffer, isAsync: useAsync);
        Assert.Equal(sendBuffer.Skip(AutoFlushThreshold).Take(2048), recvBuffer);
    }

    [Fact]
    public async Task BackpressureWorks_WithAutoFlush()
    {
        // This test verifies that the pauseWriterThreshold works correctly with auto-flush
        // by having concurrent reading and writing
        var simplex = new SimplexStream(2048, AutoFlushThreshold);

        try
        {
            byte[] sendBuffer = this.GetRandomBuffer(8192);
            byte[] recvBuffer = new byte[8192];
            int bytesRead = 0;

            // Start concurrent reader
            var readTask = Task.Run(async () =>
            {
                while (bytesRead < 8192)
                {
                    int count = await simplex.ReadAsync(recvBuffer, bytesRead, 1024, this.TimeoutToken);
                    if (count == 0)
                    {
                        break;
                    }

                    bytesRead += count;
                    await Task.Delay(10); // Simulate slow reader
                }
            });

            // Write 8KB in 1KB chunks (should auto-flush twice at 4KB and 8KB)
            for (int i = 0; i < 8; i++)
            {
                await simplex.WriteAsync(sendBuffer, i * 1024, 1024, this.TimeoutToken);
            }

            simplex.CompleteWriting();
            await readTask.WithCancellation(this.TimeoutToken);

            Assert.Equal(8192, bytesRead);
            Assert.Equal(sendBuffer, recvBuffer);
        }
        finally
        {
            simplex.Dispose();
        }
    }

    [Fact]
    public async Task Issue918_LargeWriteSmallReadWithDispose()
    {
        // This is the scenario from issue #918
        var simplex = new SimplexStream(0, 4096);

        try
        {
            int written = 0;
            var writeTask = Task.Run(async () =>
            {
                byte[] buffer = new byte[1024];
                int totalToWrite = 10 * 1024 * 1024; // 10 MB

                while (written < totalToWrite)
                {
                    await simplex.WriteAsync(buffer, 0, buffer.Length, this.TimeoutToken);
                    written += buffer.Length;
                }

                simplex.CompleteWriting();
            });

            // Read only 1KB
            byte[] readBuffer = new byte[1024];
            int bytesRead = await simplex.ReadAsync(readBuffer, 0, readBuffer.Length, this.TimeoutToken);
            Assert.Equal(1024, bytesRead);

            // Dispose the stream - this should cause the writer to fail
            simplex.Dispose();

            // Wait for writer to complete (it should fail with ObjectDisposedException or similar)
            Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => writeTask.WithCancellation(this.TimeoutToken));
            this.Logger.WriteLine($"Writer stopped after {written} bytes with: {ex.GetType().Name}: {ex.Message}");
            simplex.CompleteWriting(ex);
        }
        finally
        {
            simplex.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        this.stream.Dispose();
        base.Dispose(disposing);
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
