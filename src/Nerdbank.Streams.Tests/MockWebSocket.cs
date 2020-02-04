// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;

internal class MockWebSocket : WebSocket
{
    private Message? writingInProgress;

    private Message? readingInProgress;

    private bool closed;

    public override WebSocketCloseStatus? CloseStatus => (this.closed |= this.ReadQueue.Count == 1 && this.ReadQueue.Peek().Buffer.Length == 0) ? (WebSocketCloseStatus?)WebSocketCloseStatus.Empty : null;

    public override string CloseStatusDescription => throw new NotImplementedException();

    public override string SubProtocol => throw new NotImplementedException();

    public override WebSocketState State => throw new NotImplementedException();

    /// <summary>
    /// Gets the queue of messages to be returned from the <see cref="ReceiveAsync(ArraySegment{byte}, CancellationToken)"/> method.
    /// </summary>
    internal AsyncQueue<Message> ReadQueue { get; } = new AsyncQueue<Message>();

    internal Queue<Message> WrittenQueue { get; } = new Queue<Message>();

    internal int DisposalCount { get; private set; }

    public override void Abort() => throw new NotImplementedException();

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => Task.FromResult(0);

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => throw new NotImplementedException();

    public override void Dispose() => this.DisposalCount++;

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> output, CancellationToken cancellationToken)
    {
        Message? input = this.readingInProgress;
        if (this.readingInProgress == null)
        {
            input = this.readingInProgress = await this.ReadQueue.DequeueAsync(cancellationToken);
        }

        int bytesToCopy = Math.Min(input!.Buffer.Length, output.Count);
        input.Buffer.Slice(0, bytesToCopy).CopyTo(output.Array.AsMemory(output.Offset, output.Count));
        bool finishedMessage = bytesToCopy == input.Buffer.Length;
        if (finishedMessage)
        {
            this.readingInProgress = null;
        }
        else
        {
            input.Buffer = input.Buffer.Slice(bytesToCopy);
        }

        WebSocketReceiveResult result = new WebSocketReceiveResult(
            bytesToCopy,
            WebSocketMessageType.Text,
            finishedMessage,
            bytesToCopy == 0 ? (WebSocketCloseStatus?)WebSocketCloseStatus.Empty : null,
            bytesToCopy == 0 ? "empty" : null);
        return result;
    }

    public override Task SendAsync(ArraySegment<byte> input, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        if (this.writingInProgress == null)
        {
            byte[] bufferCopy = new byte[input.Count];
            Buffer.BlockCopy(input.Array!, input.Offset, bufferCopy, 0, input.Count);
            this.writingInProgress = new Message { Buffer = new ArraySegment<byte>(bufferCopy) };
        }
        else
        {
            Memory<byte> memory = new byte[this.writingInProgress.Buffer.Length + input.Count];
            this.writingInProgress.Buffer.CopyTo(memory);
            input.Array.CopyTo(memory.Slice(this.writingInProgress.Buffer.Length));
            this.writingInProgress.Buffer = memory;
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

    internal class Message
    {
        internal Memory<byte> Buffer { get; set; }
    }
}
