# `Stream.ReadBlockAsync(Memory<byte>, CancellationToken)`

This offers to `Stream` for reading bytes what `StreamReader.ReadBlockAsync` already offers for reading characters:
the convenient ability to read exactly the requested number of characters.

Two variants are offered:

1. `ReadBlockAsync` will return the number of bytes read, which may be less than the length of the buffer if the end of the stream is reached.
2. `ReadBlockOrThrowAsync` will always fill the entire buffer or it will throw `EndOfStreamException` if there isn't enough bytes on the stream.

The existing `Stream.ReadAsync` methods do *not* guarantee to fill a given buffer. Rather, they will initialize the
given buffer with whatever they have in their existing buffer or the first set of bytes they receive, *up to*
the specified number of bytes. So if you know you need 5 bytes, you have to create a read loop to ensure you actually
get all 5 bytes before continuing. This is extremely inconvenient, and the `ReadBlockAsync` method changes the semantic.

## Example

When you absolutely want the buffer full or it's an error condition:

```cs
var buffer = new byte[5];
await stream.ReadBlockOrThrowAsync(buffer);
// If no EndOfStreamException is thrown we know that buffer is fully initialized.
```

When you want the buffer full, but consider a premature end of stream to be acceptable:

```cs
var buffer = new byte[5];
int bytesRead = await stream.ReadBlockAsync(buffer);
if (bytesRead < 5) {
  // We encountered an end of stream before we got 5 bytes. Work with what we have.
} else {
  // We got all 5 bytes
}
```
