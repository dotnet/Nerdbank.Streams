# Exposing a Pipe as a Stream

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
await writerStream.WriterAsync(buffer, 0, buffer.Length);
```

Given an `IDuplexPipe` (which allows two-way communication over a `PipeReader`/`PipeWriter` pair),
we can wrap that as a `Stream` and use both the read and write methods on that too:

```cs
IDuplexPipe pipe; // obtained some other way
Stream duplexStream = pipe.AsStream();

// Write something
byte[] writeBuffer = { 0x1, 0x2, 0x3 };
await writerStream.WriterAsync(writeBuffer, 0, writeBuffer.Length);

// Read something
byte[] readBuffer = new byte[10];
int bytesRead = await readerStream.ReadAsync(readBuffer, 0, buffer.Length);
```
