// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class PipeStreamTests : TestBase
{
    private readonly Random random = new Random();

    private Pipe pipe;

    private Stream stream;

    public PipeStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.pipe = new Pipe();
        this.stream = new LoopbackPipe(this.pipe).AsStream();
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
        var disposableObservable = (IDisposableObservable)this.stream;
        Assert.False(disposableObservable.IsDisposed);
        disposableObservable.Dispose();
        Assert.True(disposableObservable.IsDisposed);
    }

    [Fact]
    public async Task Dispose_CompletesWriter()
    {
        TaskCompletionSource<object> completion = new TaskCompletionSource<object>();
        this.pipe.Reader.OnWriterCompleted(
            (ex, s) =>
            {
                if (ex != null)
                {
                    completion.SetException(ex);
                }
                else
                {
                    completion.SetResult(null);
                }
            },
            null);
        this.stream.Dispose();
        await completion.Task.WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task EndOfStreamCompletesReader()
    {
        var readerCompletionTask = this.pipe.Writer.WaitForReaderCompletionAsync();
        this.pipe.Writer.Complete();

        // Test ReadAsync twice because in the realization that the end of the stream is reached,
        // the state of the PipeReader changes, so we want to try it again to make sure it still works.
        Assert.Equal(0, await this.stream.ReadAsync(new byte[1], 0, 1, this.TimeoutToken));
        Assert.Equal(0, await this.stream.ReadAsync(new byte[1], 0, 1, this.TimeoutToken));
        Assert.Equal(0, this.stream.Read(new byte[1], 0, 1));
        Assert.Equal(-1, this.stream.ReadByte());
        await readerCompletionTask.WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public void Dispose_NoWriter()
    {
        // Verify that we don't throw when disposing a stream without a writer.
        var stream = this.pipe.Reader.AsStream();
        stream.Dispose();
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

        var stream = this.pipe.Writer.AsStream();
        Assert.False(stream.CanRead);
        stream = this.pipe.Reader.AsStream();
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.True(this.stream.CanWrite);
        this.stream.Dispose();
        Assert.False(this.stream.CanWrite);

        var stream = this.pipe.Writer.AsStream();
        Assert.True(stream.CanWrite);
        stream = this.pipe.Reader.AsStream();
        Assert.False(stream.CanWrite);
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

    [Fact]
    public async Task Write_ThrowsObjectDisposedException()
    {
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.WriteAsync(new byte[1], 0, 1));
#if SPAN_BUILTIN
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await this.stream.WriteAsync(new Memory<byte>(new byte[1])));
#endif
        Assert.Throws<ObjectDisposedException>(() => this.stream.Write(new byte[1], 0, 1));
    }

    [Fact]
    public async Task Write_ThrowsNotSupportedException()
    {
        this.stream = this.pipe.Reader.AsStream();
        await Assert.ThrowsAsync<NotSupportedException>(() => this.stream.WriteAsync(new byte[1], 0, 1));
#if SPAN_BUILTIN
        await Assert.ThrowsAsync<NotSupportedException>(async () => await this.stream.WriteAsync(new Memory<byte>(new byte[1])));
#endif
        Assert.Throws<NotSupportedException>(() => this.stream.Write(new byte[1], 0, 1));
    }

    [Fact]
    public async Task Read_ThrowsObjectDisposedException()
    {
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.ReadAsync(new byte[1], 0, 1));
#if SPAN_BUILTIN
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await this.stream.ReadAsync(new Memory<byte>(new byte[1])));
#endif
        Assert.Throws<ObjectDisposedException>(() => this.stream.Read(new byte[1], 0, 1));
    }

    [Fact]
    public async Task Read_ThrowsNotSupportedException()
    {
        this.stream = this.pipe.Writer.AsStream();
        await Assert.ThrowsAsync<NotSupportedException>(() => this.stream.ReadAsync(new byte[1], 0, 1));
#if SPAN_BUILTIN
        await Assert.ThrowsAsync<NotSupportedException>(async () => await this.stream.ReadAsync(new Memory<byte>(new byte[1])));
#endif
        Assert.Throws<NotSupportedException>(() => this.stream.Read(new byte[1], 0, 1));
    }

    [Theory]
    [PairwiseData]
    public async Task WriteThenRead(bool useAsync)
    {
        byte[] sendBuffer = new byte[5];
        this.random.NextBytes(sendBuffer);
        await this.WriteAsync(sendBuffer, 0, 3, useAsync);
        await this.stream.FlushAsync(this.TimeoutToken);

        byte[] recvBuffer = new byte[10];
        await this.ReadAsync(this.stream, recvBuffer, count: 3, isAsync: useAsync);
        Assert.Equal(sendBuffer.Take(3), recvBuffer.Take(3));
    }

    [Fact]
    public async Task ReadThenWrite()
    {
        byte[] recvBuffer = new byte[10];
        Task readTask = this.ReadAsync(this.stream, recvBuffer, count: 3);

        byte[] sendBuffer = new byte[5];
        this.random.NextBytes(sendBuffer);
        await this.WriteAsync(sendBuffer, 0, 2, isAsync: true);
        await this.WriteAsync(sendBuffer, 2, 1, isAsync: true);
        await this.stream.FlushAsync(this.TimeoutToken);

        await readTask;
        Assert.Equal(sendBuffer.Take(3), recvBuffer.Take(3));
    }

    [Fact]
    public async Task ReadLessThanReadResultBuffer()
    {
        await this.pipe.Writer.WriteAsync(new byte[] { 1, 2, 3 }, this.TimeoutToken);
        this.pipe.Writer.Complete();

        byte[] buffer = new byte[4];
        int count = await this.stream.ReadAsync(buffer, 0, 2, this.TimeoutToken);
        Assert.Equal(2, count); // we should get 2 because the underlying pipe has a buffer with all 3 in it.
        count = await this.stream.ReadAsync(buffer, 2, 2, this.TimeoutToken);
        Assert.Equal(1, count);
        count = await this.stream.ReadAsync(buffer, 2, 2, this.TimeoutToken);
        Assert.Equal(0, count);
        Assert.Equal(new byte[] { 1, 2, 3, 0 }, buffer);
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

    private class LoopbackPipe : IDuplexPipe
    {
        private readonly Pipe pipe;

        internal LoopbackPipe(Pipe pipe)
        {
            this.pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        }

        public PipeReader Input => this.pipe.Reader;

        public PipeWriter Output => this.pipe.Writer;
    }
}
