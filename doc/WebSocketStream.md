# WebSocket Stream

Using the `WebSocketStream` class is trivial. Simply instantiate it with the `WebSocket`
to be wrapped and it will use the `WebSocket` as a transport while emulating standard
.NET `Stream` behavior.

```cs
Stream stream = new WebSocketStream(webSocket);
```

How can now read and write on this stream as if it were your primary API to your web socket.

Flushing this stream is not necessary, as each write to the stream goes immediately
to the web socket, which has no buffer and sends all messages immediately.

## Message boundaries

Web sockets have a concept of a message boundary, such that you can send several packets
over a web socket and mark the last packet as the end of an individual message so the receiver
knows when to combine all the packets together to reconstitute the full message.
Streams do *not* have this characteristic in general and their API offers no way to
detect or create message boundaries. It is therefore recommended that a web socket be wrapped
as a .NET Stream only when message boundaries are not important to your network protocol.
