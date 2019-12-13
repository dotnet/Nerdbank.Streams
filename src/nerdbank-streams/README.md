# Nerdbank Streams

*Enhanced streams for communication in-proc or across the Internet.*

## Features

1. [`FullDuplexStream`](../../doc/FullDuplexStream.md) creates a pair of bidirectional streams for
   in-proc two-way communication; it also creates a single bidirectional stream from two
   unidirectional streams.
1. [`MultiplexingStream`](../../doc/MultiplexingStream.md) allows you to split any bidirectional
   .NET Stream into many sub-streams (called channels). This allows two parties to establish
   just one transport stream (e.g. named pipe or web socket) and use it for many independent
   protocols. For example, one might set up JSON-RPC on one channel and use other channels for
   efficient binary transfers.
1. [Substreams](../../doc/Substream.md), accessible via `writeSubstream` and `readSubstream`
   exported functions allow you to serialize data of
   an unknown length as part of a larger stream, and later deserialize it such in reading the
   substream, you cannot read more bytes than were written to it.

See our [project README][GitHubREADME] for more information.

[GitHubREADME]: https://github.com/AArnott/Nerdbank.Streams/blob/master/README.md
