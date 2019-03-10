# Prefixing buffer writer

The `PrefixingBufferWriter<T>` type implements and wraps an `IBufferWriter<T>`.
It reserves space in the wrapped `IBufferWriter<T>` for a fixed size header to be written
after some later buffer has been written.
This is particularly useful for length-prefixed lists where the length of the list is not
known until it is written out.

## Sample usage

Wrap the `IBufferWriter<T>` you have with this class, and specify the length of the header
to reserve space for. Write the payload, and then complete the operation by providing the
content for the header.

```cs
void WriteList<T>(IBufferWriter<byte> writer, IEnumerable<T> items)
{
    var prefixingWriter = new PrefixingBufferWriter<byte>(writer, sizeof(int));
    int count = 0;
    foreach (T item in items)
    {
        Serialize(prefixingWriter, item);
        count++;
    }

    // Write out the header.
    BitConverter.GetBytes(count).AsSpan().CopyTo(prefixingWriter.Prefix);

    // Commit the whole result to the underlying writer.
    prefixingWriter.Commit();
}
```

## Performance considerations

Performance in the worst case is an additional copy of some of the written buffer.
Since `IBufferWriter<T>` does not allow writing a buffer, advancing, then going back to edit
the previously advanced buffer, this wrapper does not call `Advance` on the underlying writer
until the header is known.
Given an adequately large initial buffer size, all copying is avoided. If the payload exceeds
the estimated max size, then additional buffers will be allocated internally to store the data
until the header is known, after which the additional buffers are copied into the underlying
writer's buffers and Advance is called.

## Reuse

The same `PrefixingBufferWriter<T>` instance can be used to write any number of
length-prefixed buffers. After calling `Commit` simply use it again.
