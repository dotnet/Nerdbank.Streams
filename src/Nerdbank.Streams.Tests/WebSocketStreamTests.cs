// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if ASPNETCORE
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
#endif
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class WebSocketStreamTests : TestBase
{
    private static readonly byte[] CloseRequestMessage = new byte[] { 0x1, 0x0, 0x1 };

    private Random random = new Random();

    private MockWebSocket socket;

    private WebSocketStream stream;

    public WebSocketStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.socket = new MockWebSocket();
        this.stream = new WebSocketStream(this.socket);
    }

    [Fact]
    public void Ctor_ThrowsANE()
    {
        Assert.Throws<ArgumentNullException>(() => new WebSocketStream(null));
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
    [InlineData(5)]
    [InlineData(100 * 1024)] // exceeds expected buffer size
    public async Task WriteAsync_SendsToSocket(int bufferSize)
    {
        byte[] buffer = new byte[bufferSize];
        this.random.NextBytes(buffer);
        await this.stream.WriteAsync(buffer, 0, buffer.Length, this.TimeoutToken);
        Message message = this.socket.WrittenQueue.Dequeue();
        Assert.NotSame(buffer, message.Buffer.Array);
        Assert.Equal(buffer, message.Buffer);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(100 * 1024)] // exceeds expected buffer size
    public async Task ReadAsync_ReadsFromSocket(int bufferSize)
    {
        byte[] buffer = new byte[bufferSize];
        this.random.NextBytes(buffer);
        this.socket.EnqueueRead(buffer);

        byte[] readBuffer = new byte[bufferSize];
        int bytesRead = 0;
        while (bytesRead < bufferSize)
        {
            int bytesJustRead = await this.stream.ReadAsync(readBuffer, bytesRead, readBuffer.Length - bytesRead);
            Assert.NotEqual(0, bytesJustRead);
            bytesRead += bytesJustRead;
        }

        Assert.Equal(buffer, readBuffer);
    }

    [Fact]
    public async Task WriteAsync_ThrowsObjectDisposedException()
    {
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.WriteAsync(new byte[1], 0, 1));
        Assert.Throws<ObjectDisposedException>(() => this.stream.Write(new byte[1], 0, 1));
    }

    [Fact]
    public async Task ReadAsync_ThrowsObjectDisposedException()
    {
        this.stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => this.stream.ReadAsync(new byte[1], 0, 1));
        Assert.Throws<ObjectDisposedException>(() => this.stream.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void Dispose_DisposesSocket()
    {
        this.stream.Dispose();
        Assert.Equal(1, this.socket.DisposalCount);
    }

    [Fact]
    public void Dispose_DoesNotSendCloseWebSocketPacket()
    {
        this.stream.Dispose();
        ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[500]);
        Assert.Empty(this.socket.ReadQueue);
    }

    [Fact]
    public async Task ReadAsync_ReturnsEmptyBufferWhenSocketIsClosed()
    {
        this.socket.EnqueueRead(new byte[0]);
        byte[] buffer = new byte[50];

        int bytesRead = await this.stream.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(0, bytesRead);

        // Do it again, for good measure.
        bytesRead = await this.stream.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(0, bytesRead);
    }

#if ASPNETCORE
    [Fact]
    public async Task ReadAsync_ReturnsEmptyBufferWhenSocketIsClosed_ASPNETCore()
    {
        WebSocket clientSocket;
        (this.stream, clientSocket) = await this.EstablishWebSocket();

        await this.stream.WriteAsync(CloseRequestMessage, 0, CloseRequestMessage.Length, this.TimeoutToken);
        byte[] buffer = new byte[50];

        int bytesRead = await this.stream.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(0, bytesRead);

        // Do it again, for good measure.
        bytesRead = await this.stream.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(0, bytesRead);

        Assert.False(this.stream.IsDisposed);
    }

    [Fact(Skip = "Unstable test. I'm not sure how valuable it is to verify that we get server messages *after* we've closed the connection anyway.")]
    public async Task ReadAsync_ReturnsMessagesBeforeClosing_ASPNETCore()
    {
        WebSocket clientSocket;
        (this.stream, clientSocket) = await this.EstablishWebSocket();

        // Send a buffer and immediately close the client socket, such that the server gets the message and will close it after relaying our first message.
        var sendBuffer = new byte[20];
        await this.stream.WriteAsync(sendBuffer, 0, sendBuffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        await clientSocket.CloseOutputAsync(WebSocketCloseStatus.Empty, string.Empty, this.TimeoutToken).WithCancellation(this.TimeoutToken);

        var recvBuffer = new byte[sendBuffer.Length];
        await this.ReadAsync(this.stream, recvBuffer);

        int bytesRead = await this.stream.ReadAsync(recvBuffer, 0, recvBuffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(0, bytesRead);

        // Do it again, for good measure.
        bytesRead = await this.stream.ReadAsync(recvBuffer, 0, recvBuffer.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        Assert.Equal(0, bytesRead);

        Assert.False(this.stream.IsDisposed);
    }

    [Fact]
    public async Task WriteAsync_ManyTimesWithoutAwaiting()
    {
        WebSocket clientSocket;
        (this.stream, clientSocket) = await this.EstablishWebSocket();

        const int writeCount = 10;
        var buffer = new byte[5];
        await Task.WhenAll(Enumerable.Range(0, writeCount).Select(i => this.stream.WriteAsync(buffer, 0, buffer.Length, this.TimeoutToken)));

        var recvBuffer = new byte[buffer.Length * writeCount];
        int bytesRead = 0;
        while (bytesRead < recvBuffer.Length)
        {
            int bytesJustRead = await this.stream.ReadAsync(recvBuffer, bytesRead, recvBuffer.Length - bytesRead, this.TimeoutToken).WithCancellation(this.TimeoutToken);
            Assert.NotEqual(0, bytesJustRead);
            bytesRead += bytesJustRead;
            this.Logger.WriteLine("Just received {0} bytes. Received {1} bytes in total of {2} expected bytes.", bytesJustRead, bytesRead, recvBuffer.Length);
        }
    }
#endif

#if ASPNETCORE
    private async Task<(WebSocketStream, WebSocket)> EstablishWebSocket()
    {
        IWebHostBuilder webHostBuilder = WebHost.CreateDefaultBuilder(Array.Empty<string>())
            .UseStartup<AspNetStartup>();
        TestServer testServer = new TestServer(webHostBuilder);
        WebSocketClient testClient = testServer.CreateWebSocketClient();
        WebSocket webSocket = await testClient.ConnectAsync(testServer.BaseAddress, this.TimeoutToken);

        WebSocketStream webSocketStream = new WebSocketStream(webSocket);
        return (webSocketStream, webSocket);
    }
#endif

    private class Message
    {
        internal ArraySegment<byte> Buffer { get; set; }
    }

    private class MockWebSocket : WebSocket
    {
        private Message writingInProgress;

        private bool closed;

        public override WebSocketCloseStatus? CloseStatus => (this.closed |= this.ReadQueue.Count == 1 && this.ReadQueue.Peek().Buffer.Count == 0) ? (WebSocketCloseStatus?)WebSocketCloseStatus.Empty : null;

        public override string CloseStatusDescription => throw new NotImplementedException();

        public override string SubProtocol => throw new NotImplementedException();

        public override WebSocketState State => throw new NotImplementedException();

        /// <summary>
        /// Gets the queue of messages to be returned from the <see cref="ReceiveAsync(ArraySegment{byte}, CancellationToken)"/> method.
        /// </summary>
        internal Queue<Message> ReadQueue { get; } = new Queue<Message>();

        internal Queue<Message> WrittenQueue { get; } = new Queue<Message>();

        internal int DisposalCount { get; private set; }

        public override void Abort() => throw new NotImplementedException();

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => Task.FromResult(0);

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => throw new NotImplementedException();

        public override void Dispose() => this.DisposalCount++;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> output, CancellationToken cancellationToken)
        {
            Message input = this.ReadQueue.Peek();
            int bytesToCopy = Math.Min(input.Buffer.Count, output.Count);
            Buffer.BlockCopy(input.Buffer.Array, input.Buffer.Offset, output.Array, output.Offset, bytesToCopy);
            bool finishedMessage = bytesToCopy == input.Buffer.Count;
            if (finishedMessage)
            {
                this.ReadQueue.Dequeue();
            }
            else
            {
                input.Buffer = new ArraySegment<byte>(input.Buffer.Array, input.Buffer.Offset + bytesToCopy, input.Buffer.Count - bytesToCopy);
            }

            WebSocketReceiveResult result = new WebSocketReceiveResult(
                bytesToCopy,
                WebSocketMessageType.Text,
                finishedMessage,
                bytesToCopy == 0 ? (WebSocketCloseStatus?)WebSocketCloseStatus.Empty : null,
                bytesToCopy == 0 ? "empty" : null);
            return Task.FromResult(result);
        }

        public override Task SendAsync(ArraySegment<byte> input, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (this.writingInProgress == null)
            {
                byte[] bufferCopy = new byte[input.Count];
                Buffer.BlockCopy(input.Array, input.Offset, bufferCopy, 0, input.Count);
                this.writingInProgress = new Message { Buffer = new ArraySegment<byte>(bufferCopy) };
            }
            else
            {
                byte[] bufferCopy = this.writingInProgress.Buffer.Array;
                Array.Resize(ref bufferCopy, bufferCopy.Length + input.Count);
                Buffer.BlockCopy(input.Array, input.Offset, bufferCopy, this.writingInProgress.Buffer.Count, input.Count);
                this.writingInProgress.Buffer = new ArraySegment<byte>(bufferCopy);
            }

            if (endOfMessage)
            {
                this.WrittenQueue.Enqueue(this.writingInProgress);
                this.writingInProgress = null;
            }

            return Task.FromResult(0);
        }

        internal void EnqueueRead(byte[] buffer)
        {
            this.ReadQueue.Enqueue(new Message { Buffer = new ArraySegment<byte>(buffer) });
        }
    }

#if ASPNETCORE
    private class AspNetStartup
    {
        public AspNetStartup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.Use(async (context, next) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();

                    ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[1024]);
                    while (true)
                    {
                        WebSocketReceiveResult response = await webSocket.ReceiveAsync(buffer, context.RequestAborted);
                        if (response.CloseStatus.HasValue)
                        {
                            await webSocket.CloseAsync(response.CloseStatus.Value, response.CloseStatusDescription, context.RequestAborted);
                            break;
                        }

                        if (response.Count == 3 && Enumerable.SequenceEqual(CloseRequestMessage, buffer.Array.Take(3)))
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close requested by special message", context.RequestAborted);
                            break;
                        }

                        await webSocket.SendAsync(new ArraySegment<byte>(buffer.Array, 0, response.Count), WebSocketMessageType.Binary, true, context.RequestAborted);
                    }
                }

                await next();
            });
        }
    }
#endif

}
