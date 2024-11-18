# Features

1. [`SimplexStream`](SimplexStream.md) is meant to allow two parties to communicate *one direction*.
   Anything written to the stream can subsequently be read from it. You can share this `Stream`
   with any two parties (in the same AppDomain) and one can send messages to the other.
1. [`FullDuplexStream`](FullDuplexStream.md) creates a pair of bidirectional streams for
   in-proc two-way communication; it also creates a single bidirectional stream from two
   unidirectional streams.
1. [`MultiplexingStream`](MultiplexingStream.md) allows you to split any bidirectional
   .NET Stream into many sub-streams (called channels). This allows two parties to establish
   just one transport stream (e.g. named pipe or web socket) and use it for many independent
   protocols. For example, one might set up JSON-RPC on one channel and use other channels for
   efficient binary transfers.
1. [`AsStream()`](AsStream.md) wraps a `WebSocket`, `System.IO.Pipelines.PipeReader`,
   `System.IO.Pipelines.PipeWriter`, or `System.IO.Pipelines.IDuplexPipe` with a
   `System.IO.Stream` for reading and/or writing.
1. [`UsePipe()`](UsePipe.md) enables reading from
   and writing to a `Stream` or `WebSocket` using the `PipeReader` and `PipeWriter` APIs.
1. [`Stream.ReadSlice(long)`](ReadSlice.md) creates a sub-stream that ends after
   a given number of bytes.
1. [`PipeReader.ReadSlice(long)`](ReadSlice.md) creates a sub-`PipeReader` that ends after
   a given number of bytes.
1. [`MonitoringStream`](MonitoringStream.md) wraps another Stream and raises events for
   all I/O calls so you can monitor and/or trace the data as it goes by.
1. [`WriteSubstream` and `ReadSubstream`](Substream.md) allow you to serialize data of
   an unknown length as part of a larger stream, and later deserialize it such in reading the
   substream, you cannot read more bytes than were written to it.
1. [`Sequence<T>`](Sequence.md) is a builder for `ReadOnlySequence<T>`.
1. [`PrefixingBufferWriter<T>`](PrefixingBufferWriter.md) wraps another `IBufferWriter<T>`
   to allow for prefixing some header to the next written buffer, which may be arbitrarily long.
1. [`BufferTextWriter`](BufferTextWriter.md) is a `TextWriter`-derived type that can
   write directly to any `IBufferWriter<byte>`, making it more reusable than `StreamWriter`
   and thus allows for alloc-free writing across many writers.
1. [`SequenceTextReader`](SequenceTextReader.md) is a `TextReader`-derived type that can
   read directly from any `ReadOnlySequence<byte>`, making it more reusable than `StreamReader`
   and thus allows for alloc-free reading across many sequences.
1. [`DuplexPipe`](DuplexPipe.md) is a trivial implementation of `IDuplexPipe`.
1. [`Stream.ReadBlockAsync`](ReadBlockAsync.md) guarantees to fill the supplied buffer except under certain documented conditions, instead of the regular `ReadAsync` guarantee of supplying at least 1 byte.
