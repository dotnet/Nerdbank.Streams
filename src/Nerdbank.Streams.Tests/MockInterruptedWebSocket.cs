// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

// Represents a WebSocket that is in an erroneous state and throws WebSocketException.
internal class MockInterruptedWebSocket : WebSocket
{
    public override WebSocketCloseStatus? CloseStatus => null;

    public override string CloseStatusDescription => throw new NotImplementedException();

    public override string SubProtocol => throw new NotImplementedException();

    public override WebSocketState State => throw new NotImplementedException();

    public override void Abort() => throw new NotImplementedException();

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => throw new NotImplementedException();

    public override void Dispose()
    {
    }

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> output, CancellationToken cancellationToken) =>
        throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);

    public override Task SendAsync(ArraySegment<byte> input, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
        throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
}
