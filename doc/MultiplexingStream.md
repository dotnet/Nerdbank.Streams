# Multiplexing Stream

The `MultiplexingStream` class allows for any bidirectional .NET `Stream` to
represent many "channels" of communication. Each channel may carry a different protocol
(or simply carry binary data) and is efficiently carried over the stream with no
encoding/escaping applied.

**IMPORTANT: The API for this class is still under development.**

Given a bidirectional stream between two parties, both parties should start the multiplexing layer:

```cs
Stream transportStream;
var multiplexor = MultiplexingStream.CreateAsync(this.transport1, mx1TraceSource, this.TimeoutToken);
```

You can now create individual channels that are each streams of their own. One party creates a channel
and the other party must accept it. This can happen in either order, but both sides must play their part
before either side gets a `Stream` object back.

For example, the client can create the channel:

```cs
Stream channel1 = await multiplexor.CreateChannelAsync("json-rpc", this.TimeoutToken);
```

And the server can accept it:

```cs
Stream channel1 = await multiplexor.AcceptChannelAsync("json-rpc", this.TimeoutToken);
```

This can be repeated as many times as necessary to establish channels for each protocol you use.
You might even use more than one channel for the same protocol in order to ensure that one
very large message does not starve other messages the opportunity to be transmitted, since each
channel shares the bandwidth with all other channels.
