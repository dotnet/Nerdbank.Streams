// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class NestedPipeReaderTests : TestBase, IAsyncLifetime
{
    private static readonly ReadOnlyMemory<byte> OriginalBuffer = Enumerable.Range(1, 10).Select(i => (byte)i).ToArray();
    private readonly Pipe pipe = new Pipe();

    public NestedPipeReaderTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        await this.pipe.Writer.WriteAsync(OriginalBuffer, this.TimeoutToken);
    }

    [Fact]
    public void TryRead_AllAtOnce_ExamineEverything()
    {
        const int sliceLength = 2;
        var sliceReader = this.pipe.Reader.ReadSlice(sliceLength);
        Assert.True(sliceReader.TryRead(out ReadResult readResult));
        Assert.Equal<byte>(OriginalBuffer.Slice(0, sliceLength).ToArray(), readResult.Buffer.ToArray());
        Assert.True(readResult.IsCompleted);
        sliceReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);

        // No more data is available.
        Assert.True(sliceReader.TryRead(out readResult));
        Assert.True(readResult.IsCompleted);
        Assert.Equal(sliceLength, readResult.Buffer.Length);
        sliceReader.AdvanceTo(readResult.Buffer.End, readResult.Buffer.End);

        Assert.True(sliceReader.TryRead(out readResult));
        Assert.True(readResult.IsCompleted);
        Assert.True(readResult.Buffer.IsEmpty);

        // Verify that the original PipeReader can still produce bytes.
        Assert.True(this.pipe.Reader.TryRead(out readResult));
        Assert.Equal<byte>(OriginalBuffer.Slice(sliceLength).ToArray(), readResult.Buffer.ToArray());
    }

    [Fact]
    public void TryRead_AllAtOnce()
    {
        const int sliceLength = 2;
        var sliceReader = this.pipe.Reader.ReadSlice(sliceLength);
        Assert.True(sliceReader.TryRead(out ReadResult readResult));
        Assert.Equal<byte>(OriginalBuffer.Slice(0, sliceLength).ToArray(), readResult.Buffer.ToArray());
        sliceReader.AdvanceTo(readResult.Buffer.End);

        // No more data is available, and we've reached the end of the slice so it's Complete implicitly.
        Assert.True(sliceReader.TryRead(out readResult));
        Assert.True(readResult.IsCompleted);
        Assert.Equal(0, readResult.Buffer.Length);

        // Verify that the original PipeReader can still produce bytes.
        Assert.True(this.pipe.Reader.TryRead(out readResult));
        Assert.Equal<byte>(OriginalBuffer.Slice(sliceLength).ToArray(), readResult.Buffer.ToArray());
    }

    [Fact]
    public void TryRead_SliceExceedsUnderlyingLength()
    {
        this.pipe.Writer.Complete();

        var sliceReader = this.pipe.Reader.ReadSlice(OriginalBuffer.Length + 1);
        Assert.True(sliceReader.TryRead(out ReadResult readResult));
        Assert.Equal(OriginalBuffer.Length, readResult.Buffer.Length);
        Assert.True(readResult.IsCompleted);
        sliceReader.AdvanceTo(readResult.Buffer.End);

        Assert.True(sliceReader.TryRead(out readResult));
        Assert.True(readResult.Buffer.IsEmpty);
        Assert.True(readResult.IsCompleted);
    }

    [Fact]
    public void TryRead_SliceExceedsUnderlyingLength_NotCompleted()
    {
        var sliceReader = this.pipe.Reader.ReadSlice(OriginalBuffer.Length + 1);
        Assert.True(sliceReader.TryRead(out ReadResult readResult));
        Assert.Equal(OriginalBuffer.Length, readResult.Buffer.Length);
        Assert.False(readResult.IsCompleted);
        sliceReader.AdvanceTo(readResult.Buffer.End);

        Assert.False(sliceReader.TryRead(out readResult));
        Assert.True(readResult.Buffer.IsEmpty);
        Assert.False(readResult.IsCompleted);
        Assert.False(readResult.IsCanceled);
    }

    [Fact]
    public async Task ReadAsync_AllAtOnce()
    {
        const int sliceLength = 2;
        var sliceReader = this.pipe.Reader.ReadSlice(sliceLength);
        ReadResult readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.Equal<byte>(OriginalBuffer.Slice(0, sliceLength).ToArray(), readResult.Buffer.ToArray());
        sliceReader.AdvanceTo(readResult.Buffer.End);
        readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);
        Assert.True(readResult.Buffer.IsEmpty);

        // Verify that the original PipeReader can still produce bytes.
        Assert.True(this.pipe.Reader.TryRead(out readResult));
        Assert.Equal<byte>(OriginalBuffer.Slice(sliceLength).ToArray(), readResult.Buffer.ToArray());
        Assert.False(readResult.IsCompleted);
    }

    [Fact]
    public async Task ReadAsync_ExamineEverything()
    {
        const int sliceLength = 2;
        var sliceReader = this.pipe.Reader.ReadSlice(sliceLength);
        ReadResult readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);
        Assert.Equal<byte>(OriginalBuffer.Slice(0, sliceLength).ToArray(), readResult.Buffer.ToArray());
        sliceReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);

        readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);
        Assert.Equal(sliceLength, readResult.Buffer.Length);

        sliceReader.AdvanceTo(readResult.Buffer.End);
        readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);
        Assert.True(readResult.Buffer.IsEmpty);

        // Verify that the original PipeReader can still produce bytes.
        Assert.True(this.pipe.Reader.TryRead(out readResult));
        Assert.Equal<byte>(OriginalBuffer.Slice(sliceLength).ToArray(), readResult.Buffer.ToArray());
    }

    [Fact]
    public async Task ReadAsync_MultipleReads()
    {
        int sliceLength = (int)(1.5 * OriginalBuffer.Length);
        var sliceReader = this.pipe.Reader.ReadSlice(sliceLength);
        ReadResult readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.False(readResult.IsCompleted);
        Assert.Equal<byte>(OriginalBuffer.ToArray(), readResult.Buffer.ToArray());
        sliceReader.AdvanceTo(readResult.Buffer.End);

        await this.pipe.Writer.WriteAsync(OriginalBuffer, this.TimeoutToken);

        readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.Equal<byte>(OriginalBuffer.Slice(0, sliceLength - OriginalBuffer.Length).ToArray(), readResult.Buffer.ToArray());
        Assert.True(readResult.IsCompleted);
        sliceReader.AdvanceTo(readResult.Buffer.End);

        readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.IsCompleted);
        Assert.Equal(0, readResult.Buffer.Length);

        // Verify that the original PipeReader can still produce bytes.
        Assert.True(this.pipe.Reader.TryRead(out readResult));
        Assert.Equal<byte>(OriginalBuffer.Slice(sliceLength - OriginalBuffer.Length).ToArray(), readResult.Buffer.ToArray());
    }

    [Fact]
    public async Task ReadAsync_SliceExceedsUnderlyingLength()
    {
        this.pipe.Writer.Complete();

        var sliceReader = this.pipe.Reader.ReadSlice(OriginalBuffer.Length + 1);
        var readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.Equal(OriginalBuffer.Length, readResult.Buffer.Length);
        Assert.True(readResult.IsCompleted);
        sliceReader.AdvanceTo(readResult.Buffer.End);

        readResult = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(readResult.Buffer.IsEmpty);
        Assert.True(readResult.IsCompleted);
    }

    [Fact]
    public void OnWriterCompleted_NoOps()
    {
        var sliceReader = this.pipe.Reader.ReadSlice(5);
        bool called = false;
        sliceReader.OnWriterCompleted((e, s) => called = true, null);
        this.pipe.Writer.Complete();
        Assert.False(called);
    }

    [Fact]
    public void TryRead_ThrowsAfterCompleting()
    {
        var sliceReader = this.pipe.Reader.ReadSlice(5);
        sliceReader.Complete();
        Assert.Throws<InvalidOperationException>(() => sliceReader.TryRead(out ReadResult result));
    }

    [Fact]
    public async Task ReadAsync_ThrowsAfterCompleting()
    {
        var sliceReader = this.pipe.Reader.ReadSlice(5);
        sliceReader.Complete();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sliceReader.ReadAsync(this.TimeoutToken).AsTask());
    }

    [Fact]
    public void Complete_DoesNotCompleteUnderlyingReader()
    {
        var sliceReader = this.pipe.Reader.ReadSlice(5);
        sliceReader.Complete();
        Assert.True(this.pipe.Reader.TryRead(out ReadResult result));
        Assert.Equal(OriginalBuffer.Length, result.Buffer.Length);
        this.pipe.Reader.Complete();
        Assert.Throws<InvalidOperationException>(() => this.pipe.Reader.TryRead(out result));
    }

    [Fact]
    public void Complete_WithException_DoesCompleteUnderlyingReader()
    {
        var sliceReader = this.pipe.Reader.ReadSlice(5);
        sliceReader.Complete(new Exception());
        Assert.Throws<InvalidOperationException>(() => this.pipe.Reader.TryRead(out ReadResult result));
    }

    [Fact]
    public void CancelPendingRead_UnderlyingReader_TryRead()
    {
        var sliceReader = this.pipe.Reader.ReadSlice(OriginalBuffer.Length + 1);
        sliceReader.CancelPendingRead();
        Assert.True(sliceReader.TryRead(out ReadResult result));
        Assert.True(result.IsCanceled);
        Assert.False(result.IsCompleted);
        Assert.Equal(OriginalBuffer.Length, result.Buffer.Length);

        Assert.True(sliceReader.TryRead(out result));
        Assert.Equal(OriginalBuffer.Length, result.Buffer.Length);
        Assert.False(result.IsCanceled);
        Assert.False(result.IsCompleted);
    }

    [Fact]
    public void CancelPendingRead_AfterLastUnderylingRead_TryRead()
    {
        const int sliceLength = 2;
        var sliceReader = this.pipe.Reader.ReadSlice(sliceLength);
        Assert.True(sliceReader.TryRead(out ReadResult result));
        Assert.Equal(sliceLength, result.Buffer.Length);
        Assert.False(result.IsCanceled);
        Assert.True(result.IsCompleted);
        sliceReader.AdvanceTo(result.Buffer.End);

        sliceReader.CancelPendingRead();
        Assert.True(sliceReader.TryRead(out result));
        Assert.True(result.Buffer.IsEmpty);
        Assert.True(result.IsCanceled);
        Assert.True(result.IsCompleted);

        Assert.True(sliceReader.TryRead(out result));
        Assert.True(result.Buffer.IsEmpty);
        Assert.False(result.IsCanceled);
        Assert.True(result.IsCompleted);
    }

    [Fact]
    public async Task CancelPendingRead_UnderlyingReader_ReadAsync()
    {
        var sliceReader = this.pipe.Reader.ReadSlice(OriginalBuffer.Length + 1);
        sliceReader.CancelPendingRead();
        ReadResult result = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(result.IsCanceled);
        Assert.False(result.IsCompleted);
        Assert.Equal(OriginalBuffer.Length, result.Buffer.Length);

        result = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.Equal(OriginalBuffer.Length, result.Buffer.Length);
        Assert.False(result.IsCanceled);
        Assert.False(result.IsCompleted);
    }

    [Fact]
    public async Task CancelPendingRead_AfterLastUnderylingRead_ReadAsync()
    {
        const int sliceLength = 2;
        var sliceReader = this.pipe.Reader.ReadSlice(sliceLength);
        ReadResult result = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.Equal(sliceLength, result.Buffer.Length);
        Assert.False(result.IsCanceled);
        Assert.True(result.IsCompleted);
        sliceReader.AdvanceTo(result.Buffer.End);

        sliceReader.CancelPendingRead();
        result = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(result.Buffer.IsEmpty);
        Assert.True(result.IsCanceled);
        Assert.True(result.IsCompleted);

        result = await sliceReader.ReadAsync(this.TimeoutToken);
        Assert.True(result.Buffer.IsEmpty);
        Assert.False(result.IsCanceled);
        Assert.True(result.IsCompleted);
    }
}
