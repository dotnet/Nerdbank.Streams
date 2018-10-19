# Exposing I/O as a Stream

Exposing various I/O classes as a `Stream` is trivial. Simply call the `AsStream()` extension method.

## WebSockets

```cs
Stream stream = webSocket.AsStream();
```

You can now read and write on this stream as if it were your primary API to your web socket.

Flushing this stream is not necessary, as each write to the stream goes immediately
to the web socket, which has no buffer and sends all messages immediately.

### Message boundaries

Web sockets have a concept of a message boundary, such that you can send several packets
over a web socket and mark the last packet as the end of an individual message so the receiver
knows when to combine all the packets together to reconstitute the full message.
Streams do *not* have this characteristic in general and their API offers no way to
detect or create message boundaries. It is therefore recommended that a web socket be wrapped
as a .NET Stream only when message boundaries are not important to your network protocol.

## Pipes

When you have a `System.IO.Pipelines.PipeReader`, a
`System.IO.Pipelines.PipeWriter` or a `System.IO.Pipelines.IDuplexPipe`,
you may find that you want to read/write data using `System.IO.Stream` APIs.
The `AsStream()` extension method for each of these types will return a `Stream` object
whose I/O methods will leverage the underlying pipe.

The following demonstrates wrapping a `PipeReader` in a `Stream` and reading from it:

```cs
PipeReader reader; // obtained some other way
Stream readerStream = reader.AsStream();
byte[] buffer = new byte[10];
int bytesRead = await readerStream.ReadAsync(buffer, 0, buffer.Length);
```

The following demonstrates wrapping a `PipeWriter` in a `Stream` and writing to it:

```cs
PipeWriter writer; // obtained some other way
Stream writerStream = writer.AsStream();
byte[] buffer = { 0x1, 0x2, 0x3 };
await writerStream.WriteAsync(buffer, 0, buffer.Length);
```

Given an `IDuplexPipe` (which allows two-way communication over a `PipeReader`/`PipeWriter` pair),
we can wrap that as a `Stream` and use both the read and write methods on that too:

```cs
IDuplexPipe pipe; // obtained some other way
Stream duplexStream = pipe.AsStream();

// Write something
byte[] writeBuffer = { 0x1, 0x2, 0x3 };
await writerStream.WriteAsync(writeBuffer, 0, writeBuffer.Length);

// Read something
byte[] readBuffer = new byte[10];
int bytesRead = await readerStream.ReadAsync(readBuffer, 0, buffer.Length);
```

## `ReadOnlySequence<byte>`

A `ReadOnlySequence<byte>` can be exposed as a seekable, read-only `Stream`.
This enables progressive decoding or deserializing data without allocating a single
array for the entire sequence.

```cs
ReadOnlySequence<byte> sequence; // obtained some other way
Stream stream = sequence.AsStream();
var reader = new StreamReader(stream);
while ((string line = reader.ReadLine()) != null)
{
    Console.WriteLine(line);
}
```

## `IBufferWriter<byte>`

If you're building up a `ReadOnlySequence<byte>` using `Sequence<byte>`, or have another `IBufferWriter<byte>` instance,
you can add to that sequence with `Stream` APIs using `AsStream()`:

```cs
var sequence = new Sequence<byte>();
var stream = sequence.AsStream();
stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
var readOnlySequence = sequence.AsReadOnlySequence;
Assert.Equal(3, readOnlySequence.Length);
```
