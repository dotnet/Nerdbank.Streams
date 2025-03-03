# Multiplexing Stream

The `MultiplexingStream` class allows for any bidirectional .NET `Stream` to
represent many "channels" of communication. Each channel may carry a different protocol
(or simply carry binary data) and is efficiently carried over the stream with no
encoding/escaping applied.

## Multiplexing a stream

Given a bidirectional stream between two parties, both parties should start the multiplexing layer:

```cs
Stream transportStream;
MultiplexingStream multiplexor = await MultiplexingStream.CreateAsync(
    transportStream,
    new MultiplexingStream.Options { ProtocolMajorVersion = 3 },
    this.TimeoutToken);
```

Learn more about [protocol major versions](#MajorProtocolVersions).

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

### Seeded channels

Anonymous channels may be "seeded" when creating the initial `MultiplexingStream` when using protocol version 3 or later.
This allows two communicating endpoints to establish a multiplexing stream and as many channels as they initially require without ever exchanging a network packet.
This is an optimization that increasing the coupling between two parties that should only be used when avoiding network traffic during the handshake of initial channels is required.
Each party must specify exactly the same number of seeded channels and channel options or protocol failures may result.

To establish a multiplexing stream connection that includes seeded channels, set up the stream like this:

```cs
Stream transportStream;
MultiplexingStream multiplexor = MultiplexingStream.Create(
    transportStream,
    new MultiplexingStream.Options {
        ProtocolMajorVersion = 3,
        SeededChannels = {
            new MultiplexingStream.ChannelOptions { },
            new MultiplexingStream.ChannelOptions { },
            new MultiplexingStream.ChannelOptions { },
        },
    },
    this.TimeoutToken);
```

The above sample sets up 3 channels, each with a default set of options.
Both sides must set up the same number of seeded channels with equivalent options.

To use these channels within the multiplexing stream, *accept* them as anonymous channels,
using the index into the seeded channels list as the channel ID:

```cs
Channel ch1 = multiplexor.AcceptChannel(0);
Channel ch2 = multiplexor.AcceptChannel(1);
Channel ch3 = multiplexor.AcceptChannel(2);
```

Note that *both* sides will *accept* these channels, as they are not offered by either party because they are seeded by both.
These channels cannot be rejected with `RejectChannel` but they can be shutdown after being accepted.

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

## <a name="MajorProtocolVersions"></a>Major protocol versions

Each major version of the multiplexing stream protocol represent wire-protocol breaking changes.
Using the latest version is generally preferred but should only be made when all communicating parties can be updated simultaneously.

Version | Unique capabilities
--|--
1 | Initial version
2 | Adds per-channel flow control. This prevents one channel's full buffer from pausing all other channels.
3 | Removes initial handshake. Adds seeded channels.
