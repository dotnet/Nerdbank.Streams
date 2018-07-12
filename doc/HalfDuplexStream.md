# Half-duplex Stream

The `HalfDuplexStream` class is a .NET `Stream`-derived type that allows
one party to send messages to another party that shares the same instance.

The communication is uni-directional. For bi-directional messaging, check
out [FullDuplexStream](FullDuplexStream.md).

## Sample

The following sample demonstrates creation and use of this stream
for one party to send messages to another, and how to indicate
when communication is over.

```csharp
var stream = new HalfDuplexStream();
var writer = new StreamWriter(stream);
var reader = new StreamReader(stream);

var speaker = Task.Run(async delegate {
    await writer.WriteLineAsync("Hello, listener 1!");
    await writer.WriteLineAsync("Hello, listener 2!");
    stream.CompleteWriting();
});

var listener = Task.Run(async delegate {
    string line;
    while ((line = await reader.ReadLineAsync()) != null) {
        Console.WriteLine("I received: " + line);
    }
});

await Task.WhenAll(speaker, listener);
Console.WriteLine("...and we're done!");
```

Disposing of the `HalfDuplexStream` prevents any further writing to
or reading from the stream. No native resources are held by this
time so disposing is not strictly necessary.
However calling `CompleteWriting()` is highly encouraged so any waiting readers will return, recognizing no more data will come.
