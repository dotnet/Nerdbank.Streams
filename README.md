Specialized .NET Stream classes
=========================

[![NuGet package](https://img.shields.io/nuget/v/Nerdbank.Streams.svg)](https://nuget.org/packages/Nerdbank.Streams)

[![Build status](https://ci.appveyor.com/api/projects/status/849r1unf4tnjbpy8?svg=true)](https://ci.appveyor.com/project/AArnott/nerdbank-fullduplexstream)
[![codecov](https://codecov.io/gh/AArnott/Nerdbank.Streams/branch/master/graph/badge.svg)](https://codecov.io/gh/AArnott/Nerdbank.Streams)

## Features

The streams in this package focus on communication (not persistence).

1. [`HalfDuplexStream`](doc/HalfDuplexStream.md) is meant to allow two parties to communicate *one direction*.
   Anything written to the stream can subsequently be read from it. You can share this `Stream`
   with any two parties (in the same AppDomain) and one can send messages to the other.
2. [`FullDuplexStream`](doc/FullDuplexStream.md) provides *two* `Stream` objects, which you
   assign out to each of two parties. These parties can now *send and receive* messages
   with each other by reading from and writing to their assigned `Stream` instance.
3. [`MultiplexingStream`](doc/MultiplexingStream.md) allows you to split any bidirectional
   .NET Stream into many sub-streams (called channels). This allows two parties to establish
   just one transport stream (e.g. named pipe or web socket) and use it for many independent
   protocols. For example, one might set up JSON-RPC on one channel and use other channels for
   efficient binary transfers.
4. [`WebSocketStream`](doc/WebSocketStream.md) wraps a `WebSocket` to conform to a `Stream`
   API for interop with many APIs that accept streams.
5. [`PipeStream`](doc/PipeStream.md) wraps a `System.IO.Pipelines.PipeReader` and/or
   `System.IO.Pipelines.PipeWriter` within the API of a `System.IO.Stream`.
6. [`Stream.ReadSlice(int)`](doc/ReadSlice.md) creates a sub-stream that ends after
   a given number of bytes.
