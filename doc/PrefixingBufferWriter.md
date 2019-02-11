# Prefixing buffer writer

The `PrefixingBufferWriter<T>` type implements and wraps an `IBufferWriter<T>`.
It reserves space in the wrapped `IBufferWriter<T>` for a fixed size header to be written
after some later buffer has been written.
This is particularly useful for length-prefixed lists where the length of the list is not
known until it is written out.

## Performance considerations

Performance in the worst case is an additional copy of some of the written buffer.
Since `IBufferWriter<T>` does not allow writing a buffer, advancing, then going back to edit
the previously advanced buffer, this wrapper does not call `Advance` on the underlying writer
until the header is known.
Given an adequately large initial buffer size, all copying is avoided. If the payload exceeds
the estimated max size, then additional buffers will be allocated internally to store the data
until the header is known, after which the additional buffers are copied into the underlying
writer's buffers and Advance is called.
