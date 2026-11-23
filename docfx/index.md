# Overview

Nerdbank.Streams is a .NET library with a collection of APIs to make using the streaming/pipelines/buffers APIs of .NET easier.

Install it from NuGet:

[![NuGet package](https://img.shields.io/nuget/v/Nerdbank.Streams.svg)](https://www.nuget.org/packages/Nerdbank.Streams)

```console
dotnet add package Nerdbank.Streams
```

## Highlights

- [SimplexStream](docs/SimplexStream.md) provides one-way in-process communication between two parties.
- [FullDuplexStream](docs/FullDuplexStream.md) creates bidirectional streams for two-way in-process communication.
- [MultiplexingStream](docs/MultiplexingStream.md) splits one transport stream into multiple independent channels.
- [AsStream](docs/AsStream.md) adapts websockets and pipelines to familiar stream APIs.
- [Sequence](docs/Sequence.md) efficiently builds segmented byte sequences.
- [SharedMemoryDuplexPipe](docs/SharedMemoryDuplexPipe.md) transfers bytes between trusted processes without a kernel copy.

See [Features](docs/features.md) for the full list.

## Getting started

[Get started](docs/getting-started.md) with installation and a first example.
