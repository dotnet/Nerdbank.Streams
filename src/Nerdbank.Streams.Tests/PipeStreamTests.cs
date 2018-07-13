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

    private PipeStream stream;

    public PipeStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        // This stream will act as a loopback
        this.pipe = new Pipe();
        this.stream = new PipeStream(this.pipe.Writer, this.pipe.Reader);
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
    public void Dispose_NoWriter()
    {
        // Verify that we don't throw when disposing a stream without a writer.
        PipeStream stream = new PipeStream(writer: null, reader: this.pipe.Reader);
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

        PipeStream stream = new PipeStream(this.pipe.Writer, reader: null);
        Assert.False(stream.CanRead);
        stream = new PipeStream(null, reader: this.pipe.Reader);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.True(this.stream.CanWrite);
        this.stream.Dispose();
        Assert.False(this.stream.CanWrite);

        PipeStream stream = new PipeStream(this.pipe.Writer, reader: null);
        Assert.True(stream.CanWrite);
        stream = new PipeStream(null, reader: this.pipe.Reader);
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

    [Theory]
    [PairwiseData]
    public async Task Write_ThrowsObjectDisposedException(bool useAsync)
    {
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.WriteAsync(new byte[1], 0, 1, useAsync));
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
