# `TextWriter` for `IBufferWriter<byte>` 

Given an `IBufferWriter<byte>`, an adapter to progressively write text to it
can be useful. Many APIs already accept a `TextWriter` to write text to.
`Console.Out` and `StreamWriter` are `TextWriter`s, for example.

The `BufferTextWriter` class derives from `TextWriter` and accepts an
`IBufferWriter<byte>` in order to enable writing text to a byte buffer or stream.

You can also use the [`IBufferWriter<byte>.AsStream()`](AsStream.md) extension method to create a `Stream`
and pass that as an argument when constructing a new `StreamWriter`, but creating a `StreamWriter`
allocates a couple of large `byte[]` and `char[]` arrays which make this an expensive approach,
particularly when you need to read from many unique `IBufferWriter<byte>` instances and thus
have to repeatedly create a new `StreamWriter` and its own buffers each time.

`BufferTextWriter` avoids these costs by optimizing for reading directly from the
`IBufferWriter<byte>` (avoiding the `Stream` intermediate adapter) and by allowing
the same `BufferTextWriter` instance to be assigned different `IBufferWriter<byte>`
over time, allowing reuse of the buffers it uses internally during the encoding process.

## Example

```cs
private readonly BufferTextWriter textWriter = new BufferTextWriter();

void ReadTextFromConsole(IBufferWriter<byte> byteWriter)
{
    textWriter.Initialize(byteWriter, Encoding.UTF8);
    string line;
    while ((line = Console.In.ReadLine()) != null)
    {
        textWriter.WriteLine(line);
    }
}
```
