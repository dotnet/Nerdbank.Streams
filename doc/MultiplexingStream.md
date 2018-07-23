# Multiplexing Stream

The `MultiplexingStream` class allows for any bidirectional .NET `Stream` to
represent many "channels" of communication. Each channel may carry a different protocol
(or simply carry binary data) and is efficiently carried over the stream with no
encoding/escaping applied.

## Multiplexing a stream

Given a bidirectional stream between two parties, both parties should start the multiplexing layer:

```cs
Stream transportStream;
var multiplexor = MultiplexingStream.CreateAsync(transportStream, this.TimeoutToken);
```

## Establishing named channels

You can now create individual channels that are each streams of their own. One party creates a channel
and the other party must accept it. This can happen in either order, but both sides must play their part
before either side gets a `Stream` object back.

For example, the client can create some channels:

```cs
Channel rpcChannel = await multiplexor.OfferChannelAsync("json-rpc", this.TimeoutToken);
Channel binChannel = await multiplexor.OfferChannelAsync("binarydata", this.TimeoutToken);
```

And the server can accept them:

```cs
Channel rpcChannel = await multiplexor.AcceptChannelAsync("json-rpc", this.TimeoutToken);
Channel binChannel = await multiplexor.AcceptChannelAsync("binarydata", this.TimeoutToken);
```

This can be repeated as many times as necessary to establish channels for each protocol you use.
You might even use more than one channel for the same protocol in order to ensure that one
very large message does not starve other messages the opportunity to be transmitted, since each
channel shares the bandwidth with all other channels.

## Communicating over channels

`Channel` implements `IDuplexPipe` which provides access to its most efficient means of
transmitting and receiving data:

```cs
await binChannel.Output.WriteAsync(new byte[] { 1, 2, 3 });
ReadResult readResult = await binChannel.Input.ReadAsync();
```

To use `Channel` with code that requires a `Stream`, simply use the `Channel`.[AsStream()](AsStream.md) extension method.

```cs
Stream rpcBidirectionalStream = rpcChannel.AsStream();
var rpc = JsonRpc.Attach(rpcBidirectionalStream, new RpcServer()); // JsonRpc is defined in the StreamJsonRpc library
```

## Establishing ephemeral/anonymous channels

In the course of using a `Channel` it may be useful to establish another one.
For example when communicating over one channel with JSON-RPC, one may need
to transmit a large amount of binary data. Since JSON is an inefficient and
often slow protocol for transmitting large blobs of binary data, creating an
ephemeral channel dedicated to sending just that data can be a great use of
the multiplexing stream.

```cs
// Side A
var blobChannel = multiplexor.CreateChannel();
await rpc.SendBlobAsync(blobChannel.Id);

// Side B
class Server {
    async Task SendBlobAsync(int channelId) {
        Channel blobChannel = this.multiplexor.AcceptChannel(channelId);
    }
}
```

Ephemeral/anonymous channels are just like regular channels except that you
cannot name them. Establishing an ephemeral channel is unique in that the
`CreateChannel` method returns immediately instead of waiting for the remote
party to accept the channel. This facilitates extracting the `Channel.Id` by
the originating party in order that they may communicate the ID to the remote
party and they can accept it within some context defined by that application.
Accepting an ephemeral channel is similarly exposed as a synchronous method
as it never waits for the offer -- the offer is presumed to already be waiting
for acceptance. This is guaranteed as long as the ID is communicated at the app
level using the multiplexing stream, since before that app-level message could
have arrived, the channel offer would have already been transmitted by that
same stream.

## Shutting down

### Complete writing

A cooperative shutdown of a `Channel` may begin with one party indicating that they are done writing.

```cs
rpcChannel.Output.Complete();
```

Doing this while keeping the `Channel` open allows any bytes that the remote party may still be sending
to arrive.

A channel that has completed writing and received a writing completed message from the remote party automatically disposes itself.

### Disposing a channel

A `Channel` may be immediately shut down by disposing it:`

```cs
rpcChannel.Dispose();
```

Closing a channel is an event that is communicated with the remote party.
Any attempt by the remote party to read more bytes from the channel will signal an end of stream
by returning 0 bytes from any requests to read from the stream.

Disposing a channel takes effect immediately for the local party. Any bytes transmitted by the remote party
that have not already been read become inaccessible.

Disposing the `MultiplexingStream` instance itself will dispose all the open channels.
