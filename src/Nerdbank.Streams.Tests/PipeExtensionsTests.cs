// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipes;
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
using IPC = System.IO.Pipes;

public partial class PipeExtensionsTests : TestBase
{
    public PipeExtensionsTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void UsePipeReader_WebSocket_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => PipeExtensions.UsePipeReader((WebSocket)null!));
    }

    [Fact]
    public void UsePipeWriter_WebSocket_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => PipeExtensions.UsePipeWriter((WebSocket)null!));
    }

    [Fact]
    public async Task UsePipe_Stream()
    {
        var ms = new HalfDuplexStream();
        IDuplexPipe pipe = ms.UsePipe(cancellationToken: this.TimeoutToken);
        await pipe.Output.WriteAsync(new byte[] { 1, 2, 3 }, this.TimeoutToken);
        var readResult = await pipe.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(3, readResult.Buffer.Length);
        pipe.Input.AdvanceTo(readResult.Buffer.End);
    }

    [Fact]
    public async Task UsePipe_Stream_Disposal()
    {
        var ms = new HalfDuplexStream();
        IDuplexPipe pipe = ms.UsePipe(cancellationToken: this.TimeoutToken);
        pipe.Output.Complete();
        pipe.Input.Complete();
        await this.AssertStreamClosesAsync(ms);
    }

    /// <summary>
    /// Verify that completing the <see cref="PipeReader"/> and <see cref="PipeWriter"/> lead to the disposal of the
    /// IPC <see cref="Stream"/> on both sides.
    /// </summary>
    /// <remarks>
    /// This is worth a special test because on .NET Framework, IPC stream reads are not cancelable.
    /// </remarks>
    [Fact]
    public async Task UsePipe_IpcPipeStream_Disposal()
    {
        var guid = Guid.NewGuid().ToString();

        var ipcServerTask = Task.Run(async delegate
        {
            using var ipcServerPipe = new NamedPipeServerStream(guid, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, IPC.PipeOptions.Asynchronous);
            await ipcServerPipe.WaitForConnectionAsync(this.TimeoutToken);
            int bytesRead = await ipcServerPipe.ReadAsync(new byte[1], 0, 1, this.TimeoutToken).WithCancellation(this.TimeoutToken);
            ipcServerPipe.Dispose();
            this.Logger.WriteLine("The server stream closed.");
        });
        var ipcClientTask = Task.Run(async delegate
        {
            using var ipcClientPipe = new NamedPipeClientStream(".", guid, IPC.PipeDirection.InOut, IPC.PipeOptions.Asynchronous);
            await ipcClientPipe.ConnectAsync(this.TimeoutToken);

            // We need to time this so that we don't call Complete() until reading from the PipeStream has already started.
            // Use our MonitoringStream for this purpose, and also to know when Dispose is called.
            var monitoredStream = new MonitoringStream(ipcClientPipe);
            var disposed = new TaskCompletionSource<object?>();
            var readStarted = new TaskCompletionSource<object?>();
            monitoredStream.Disposed += (s, e) => disposed.SetResult(null);
            monitoredStream.WillRead += (s, e) => readStarted.SetResult(null);
            monitoredStream.WillReadMemory += (s, e) => readStarted.SetResult(null);
            monitoredStream.WillReadSpan += (s, e) => readStarted.SetResult(null);

            IDuplexPipe pipe = monitoredStream.UsePipe(cancellationToken: this.TimeoutToken);
            await readStarted.Task.WithCancellation(this.TimeoutToken);

            pipe.Output.Complete();
            pipe.Input.Complete();

            await disposed.Task.WithCancellation(this.TimeoutToken);
            this.Logger.WriteLine("The client stream closed.");
        });

        await WhenAllSucceedOrAnyFail(ipcClientTask, ipcServerTask);
    }

    [Theory]
    [PairwiseData]
    public async Task UsePipe_Stream_OneDirectionDoesNotDispose(bool completeOutput)
    {
        var ms = new HalfDuplexStream();
        IDuplexPipe pipe = ms.UsePipe(cancellationToken: this.TimeoutToken);
        if (completeOutput)
        {
            pipe.Output.Complete();
        }
        else
        {
            pipe.Input.Complete();
        }

        var timeout = ExpectedTimeoutToken;
        while (!ms.IsDisposed && !timeout.IsCancellationRequested)
        {
            await Task.Yield();
        }

        Assert.False(ms.IsDisposed);
    }

    [Fact]
    public async Task UsePipe_Stream_PropagatesException()
    {
        var stream = new MockInterruptedFullDuplexStream();
        IDuplexPipe pipe = stream.UsePipe(cancellationToken: this.TimeoutToken);

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            while (!this.TimeoutToken.IsCancellationRequested)
            {
                var readResult = await pipe.Input.ReadAsync(this.TimeoutToken);
                pipe.Input.AdvanceTo(readResult.Buffer.End);
            }
        });
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            while (!this.TimeoutToken.IsCancellationRequested)
            {
                await pipe.Output.WriteAsync(new byte[1], this.TimeoutToken);
            }
        });
    }

    [Fact]
    public async Task UsePipe_Stream_ReadOnlyStream()
    {
        var streamPair = FullDuplexStream.CreatePair();

        byte[] expected = new byte[] { 1, 2, 3 };
        await streamPair.Item2.WriteAsync(expected, 0, expected.Length, this.TimeoutToken);
        await streamPair.Item2.FlushAsync(this.TimeoutToken);

        var readOnlyStream = new OneWayStreamWrapper(streamPair.Item1, canRead: true);
        var duplexPipe = readOnlyStream.UsePipe();
        var readResult = await duplexPipe.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(expected, readResult.Buffer.ToArray());

        Assert.Throws<InvalidOperationException>(() => duplexPipe.Output.GetSpan());

        // Complete reading and verify stream closed.
        duplexPipe.Input.Complete();
        await this.AssertStreamClosesAsync(streamPair.Item1);
    }

    [Fact]
    public async Task UsePipe_Stream_WriteOnlyStream()
    {
        var streamPair = FullDuplexStream.CreatePair();
        var writeOnlyStream = new OneWayStreamWrapper(streamPair.Item1, canWrite: true);
        var duplexPipe = writeOnlyStream.UsePipe();

        // Verify that reading isn't allowed.
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await duplexPipe.Input.ReadAsync(this.TimeoutToken));

        byte[] expected = new byte[] { 1, 2, 3 };
        await duplexPipe.Output.WriteAsync(expected, this.TimeoutToken);
        duplexPipe.Output.Complete();

        int totalBytesRead = 0;
        byte[] readBytes = new byte[10];
        int bytesRead;
        do
        {
            bytesRead = await streamPair.Item2.ReadAsync(readBytes, totalBytesRead, readBytes.Length - totalBytesRead, this.TimeoutToken);
            this.Logger.WriteLine("Read {0} bytes", bytesRead);
            totalBytesRead += bytesRead;
        }
        while (bytesRead > 0);

        Assert.Equal(expected, readBytes.Take(totalBytesRead));

        // Complete writing and verify stream closed.
        duplexPipe.Output.Complete();
        await this.AssertStreamClosesAsync(streamPair.Item1);
    }

    [Fact]
    public async Task UsePipeReader_WebSocket()
    {
        var expectedBuffer = new byte[] { 4, 5, 6 };
        var webSocket = new MockWebSocket();
        webSocket.EnqueueRead(expectedBuffer);
        var pipeReader = webSocket.UsePipeReader(cancellationToken: this.TimeoutToken);
        var readResult = await pipeReader.ReadAsync(this.TimeoutToken);
        Assert.Equal(expectedBuffer, readResult.Buffer.First.Span.ToArray());
        pipeReader.AdvanceTo(readResult.Buffer.End);
    }

    [Fact]
    public async Task UsePipeWriter_WebSocket()
    {
        var expectedBuffer = new byte[] { 4, 5, 6 };
        var webSocket = new MockWebSocket();
        var pipeWriter = webSocket.UsePipeWriter(cancellationToken: this.TimeoutToken);
        await pipeWriter.WriteAsync(expectedBuffer, this.TimeoutToken);
        pipeWriter.Complete();
        await pipeWriter.WaitForReaderCompletionAsync();
        var message = webSocket.WrittenQueue.Dequeue();
        Assert.Equal(expectedBuffer, message.Buffer.ToArray());
    }

    [Fact]
    public async Task UsePipe_WebSocket()
    {
        var expectedBuffer = new byte[] { 4, 5, 6 };
        var webSocket = new MockWebSocket();
        webSocket.EnqueueRead(expectedBuffer);
        var pipe = webSocket.UsePipe(cancellationToken: this.TimeoutToken);

        var readResult = await pipe.Input.ReadAsync(this.TimeoutToken);
        Assert.Equal(expectedBuffer, readResult.Buffer.First.Span.ToArray());
        pipe.Input.AdvanceTo(readResult.Buffer.End);

        await pipe.Output.WriteAsync(expectedBuffer, this.TimeoutToken);
        pipe.Output.Complete();
        await pipe.Output.WaitForReaderCompletionAsync();
        var message = webSocket.WrittenQueue.Dequeue();
        Assert.Equal(expectedBuffer, message.Buffer.ToArray());
    }

    [Fact]
    public async Task UsePipe_WebSocket_PropagatesException()
    {
        var webSocket = new MockInterruptedWebSocket();
        IDuplexPipe pipe = webSocket.UsePipe(cancellationToken: this.TimeoutToken);

        await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            while (!this.TimeoutToken.IsCancellationRequested)
            {
                var readResult = await pipe.Input.ReadAsync(this.TimeoutToken);
                pipe.Input.AdvanceTo(readResult.Buffer.End);
            }
        });
        await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            while (!this.TimeoutToken.IsCancellationRequested)
            {
                await pipe.Output.WriteAsync(new byte[1], this.TimeoutToken);
            }
        });
    }

    [Fact]
    public void UsePipe_CollapseAdapterStacks()
    {
        var pipes = FullDuplexStream.CreatePipePair();
        var stream = pipes.Item1.AsStream();
        var pipeAgain = stream.UsePipe(allowUnwrap: true);
        Assert.Same(pipes.Item1.Input, pipeAgain.Input);
        Assert.Same(pipes.Item1.Output, pipeAgain.Output);
    }

    private async Task AssertStreamClosesAsync(Stream stream)
    {
        Requires.NotNull(stream, nameof(stream));

        Func<bool> isDisposed =
            stream is IDisposableObservable observableStream ? new Func<bool>(() => observableStream.IsDisposed) :
            stream is PipeStream pipeStream ? new Func<bool>(() => !pipeStream.IsConnected) :
            new Func<bool>(() => !stream.CanRead && !stream.CanWrite);

        while (!this.TimeoutToken.IsCancellationRequested && !isDisposed())
        {
            await Task.Yield();
        }

        Assert.True(isDisposed());
    }
}
