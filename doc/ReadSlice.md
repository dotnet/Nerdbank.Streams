# Read slice streams

The `Stream.ReadSlice(int)` extension methods returns a `Stream` instance that will only read up to a fixed set of bytes from an underlying `Stream`.
This allows you, for example, to pass a `Stream` to a deserializer that will read to the end of the stream, even though your larger stream has more than your deserializer should see.

## Sample

In the following example, `ReadSlice` is used to allow access to just 10 bytes of a larger stream:

```cs
var largeStream = new MemoryStream(new byte[20]);
var sliceStream = largeStream.ReadSlice(10);

// Creating the nested stream does NOT read from or buffer the underlying stream.
Assert.Equal(0, largeStream.Position);

// Attempting to read more bytes from the nested stream than are allowed results in just the allowed bytes.
int bytesRead = sliceStream.Read(new byte[20], 0, 20);
Assert.Equal(10, bytesRead);

// Attempting to read further produces 0 bytes, signaling the end of the (nested) stream.
bytesRead = sliceStream.Read(new byte[20], 0, 20);
Assert.Equal(0, bytesRead);

// The underlying stream is positioned just after the nested stream.
Assert.Equal(10, largeStream.Position);
```
