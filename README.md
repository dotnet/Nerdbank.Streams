Specialized .NET Stream classes
=========================

[![NuGet package](https://img.shields.io/nuget/v/Nerdbank.Streams.svg)](https://nuget.org/packages/Nerdbank.Streams)

[![Build status](https://ci.appveyor.com/api/projects/status/849r1unf4tnjbpy8?svg=true)](https://ci.appveyor.com/project/AArnott/nerdbank-fullduplexstream)
[![Travis (.org)](https://api.travis-ci.org/AArnott/Nerdbank.Streams.svg?branch=master)](https://travis-ci.org/AArnott/Nerdbank.Streams)
[![codecov](https://codecov.io/gh/AArnott/Nerdbank.Streams/branch/master/graph/badge.svg)](https://codecov.io/gh/AArnott/Nerdbank.Streams)

## Features

The streams in this package focus on communication (not persistence).

1. [`HalfDuplexStream`](doc/HalfDuplexStream.md) is meant to allow two parties to communicate *one direction*.
   Anything written to the stream can subsequently be read from it. You can share this `Stream`
   with any two parties (in the same AppDomain) and one can send messages to the other.
1. [`FullDuplexStream`](doc/FullDuplexStream.md) creates a pair of bidirectional streams for
   in-proc two-way communication; it also creates a single bidirectional stream from two
   unidirectional streams.
1. [`MultiplexingStream`](doc/MultiplexingStream.md) allows you to split any bidirectional
   .NET Stream into many sub-streams (called channels). This allows two parties to establish
   just one transport stream (e.g. named pipe or web socket) and use it for many independent
   protocols. For example, one might set up JSON-RPC on one channel and use other channels for
   efficient binary transfers.
1. [`AsStream()`](doc/AsStream.md) wraps a `WebSocket`, `System.IO.Pipelines.PipeReader`,
   `System.IO.Pipelines.PipeWriter`, or `System.IO.Pipelines.IDuplexPipe` with a
   `System.IO.Stream` for reading and/or writing.
1. [`UsePipe()`](doc/UsePipe.md) enables reading from
   and writing to a `Stream` or `WebSocket` using the `PipeReader` and `PipeWriter` APIs.
1. [`Stream.ReadSlice(int)`](doc/ReadSlice.md) creates a sub-stream that ends after
   a given number of bytes.
1. [`MonitoringStream`](doc/MonitoringStream.md) wraps another Stream and raises events for
   all I/O calls so you can monitor and/or trace the data as it goes by.
1. [`WriteSubstream` and `ReadSubstream`](doc/Substream.md) allow you to serialize data of
   an unknown length as part of a larger stream, and later deserialize it such in reading the
   substream, you cannot read more bytes than were written to it.
1. [`Sequence<T>`](doc/Sequence.md) is a builder for `ReadOnlySequence<T>`.
