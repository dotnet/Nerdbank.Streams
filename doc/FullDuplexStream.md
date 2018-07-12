# Full-duplex streams

The `FullDuplexStream` class provides a pair of streams, each of which can be handed to one of two parties.
Each stream can be read from and written to, and these operations are used to exchange messages with the party
holding the other stream in the pair.
The two stream instances communicate with each other to facilitate this bidirectional message passing.

The code to set up a full duplex stream is trivial:

```cs
Tuple<Stream, Stream> tuple = FullDuplexStream.CreateStreams();
Task party1Simulation = Party1Async(tuple.Item1);
Task party2Simulation = Party2Async(tuple.Item2);
```

In the above code, we create a pair of streams. Each goes to one of two parties.
They can each read and write to their stream to communicate with the other party,
who uses their own stream. The two streams in the returned Tuple are interconnected.

## What and why of full duplex streams

.NET streams are great for accessing files, or communicating with a remote party.
They can also be useful for bidirectional (i.e. full duplex) communication between
two parties. .NET named pipes demonstrate such a use of .NET Stream.
When using Stream in this way between two parties, each party gets one unique instance
of Stream. Party 1 can write to their Stream to send messages to Party 2 and read
from that same stream to receive messages from Party 2.
Party 2 can likewise read and write to their stream to exchange messages with Party 1.
Although each party has their own unique instance of Stream, the two streams are
connected such that writing to one allows that data to be read from the other.

## Uses of full duplex Streams

* Testing a networking protocol by simulating both ends of communication.
* Named pipe-like communication between two in-proc components.

## FAQ

### Why not just use MemoryStream?

A single MemoryStream has a Position property, which advances whenever the Stream is
read from or written to. So if one party writes to the stream, the Position is advanced
to the end of the written data, such that if the other party tried to read from the stream,
they would not see the data previously written by the other party.

### Why not just use named pipes?

Named pipes carry the overhead of operating system handles and are not portable.
The concept of full duplex streaming is simple and can be implemented in a portable way.
