# `TextReader` for `ReadOnlySequence<byte>`

Given a `ReadOnlySequence<byte>`, an adapter to progressively read it as text
can be useful. Many APIs already accept a `TextReader` to read text.
`Console.In` and `StreamReader` are `TextReader`s, for example.

The `SequenceTextReader` class derives from `TextReader` and accepts a
`ReadOnlySequence<byte>` in order to enable reading text from a byte sequence.

You can also use the [`ReadOnlySequence<byte>.AsStream()`](AsStream.md) extension method to create a `Stream`
and pass that as an argument when constructing a new `StreamReader`, but creating a `StreamReader`
allocates a couple of large `byte[]` and `char[]` arrays which make this an expensive approach,
particularly when you need to read from many unique `ReadOnlySequence<byte>` instances and thus
have to repeatedly create a new `StreamReader` and its own buffers each time.

`SequenceTextReader` avoids these costs by optimizing for reading directly from the
`ReadOnlySequence<byte>` (avoiding the `Stream` intermediate adapter) and by allowing
the same `SequenceTextReader` instance to be assigned different `ReadOnlySequence<byte>`
over time, allowing reuse of the buffers it uses internally during the decoding process.

## Example

```cs
private readonly SequenceTextReader sequenceReader = new SequenceTextReader();

void PrintTextToConsole(ReadOnlySequence<byte> sequence)
{
    sequenceReader.Initialize(sequence, Encoding.UTF8);
    string line;
    while ((line = sequenceReader.ReadLine()) != null)
    {
        Console.WriteLine(line);
    }
}
```
