// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1414 // Tuple types in signatures should have element names

public partial class WebSocketStreamTests : TestBase
{
    private static readonly byte[] CloseRequestMessage = new byte[] { 0x1, 0x0, 0x1 };

    private Random random = new Random();

    private MockWebSocket socket;

    private Stream stream;

    public WebSocketStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.socket = new MockWebSocket();
        this.stream = this.socket.AsStream();
    }

    [Fact]
    public void Ctor_ThrowsANE()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.AsStream((WebSocket)null!));
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
        Assert.False(((IDisposableObservable)this.stream).IsDisposed);
        this.stream.Dispose();
        Assert.True(((IDisposableObservable)this.stream).IsDisposed);
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
        var message = this.socket.WrittenQueue.Dequeue();
        Assert.Equal(buffer, message.Buffer.ToArray());
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
        Assert.Equal(0, this.socket.ReadQueue.Count);
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

        Assert.False(((IDisposableObservable)this.stream).IsDisposed);
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

        Assert.False(((IDisposableObservable)this.stream).IsDisposed);
    }

    [Fact]
    public async Task WriteAsync_ManyTimesWithoutAwaiting()
    {
        WebSocket clientSocket;
        (this.stream, clientSocket) = await this.EstablishWebSocket();

        const int writeCount = 10;
        var buffer = new byte[5];
        await Task.WhenAll(Enumerable.Range(0, writeCount).Select(i => this.stream.WriteAsync(buffer, 0, buffer.Length, this.TimeoutToken)));
        await this.stream.FlushAsync(this.TimeoutToken);

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

    private async Task<(Stream, WebSocket)> EstablishWebSocket()
    {
        IWebHostBuilder webHostBuilder = WebHost.CreateDefaultBuilder(Array.Empty<string>())
            .UseStartup<AspNetStartup>();
        TestServer testServer = new TestServer(webHostBuilder);
        WebSocketClient testClient = testServer.CreateWebSocketClient();
        WebSocket webSocket = await testClient.ConnectAsync(testServer.BaseAddress, this.TimeoutToken);

        Stream webSocketStream = webSocket.AsStream();
        return (webSocketStream, webSocket);
    }

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

                        if (response.Count == 3 && Enumerable.SequenceEqual(CloseRequestMessage, buffer.Array!.Take(3)))
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close requested by special message", context.RequestAborted);
                            break;
                        }

                        await webSocket.SendAsync(new ArraySegment<byte>(buffer.Array!, 0, response.Count), WebSocketMessageType.Binary, true, context.RequestAborted);
                    }
                }

                await next();
            });
        }
    }
}
