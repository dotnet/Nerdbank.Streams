# Substreams

A substream is a `Stream` within another `Stream`.
A top-level `Stream` can contain any number of substreams. Each one has an arbitrary length
that need not be known when they start being written.
When reading these substreams, they terminate just as any normal `Stream` would, allowing
you to deserialize using a reader that naturally tries to read to the end of a `Stream`
without risk of it reading too far.

Below is a sample of a stream writer and reader pair of methods that
utilize another serialization routine that expects to deserialize the entire
stream, so we use substreams to do it.

```cs
async Task WriteStreamAsync(Stream underlyingStream)
{
    Substream substream = underlyingStream.WriteSubstream();
    await o1.SerializeAsync(substream);
    await substream.DisposeAsync();
    
    Substream substream = underlyingStream.WriteSubstream();
    await o2.SerializeAsync(substream);
    await substream.DisposeAsync();
}

async Task ReadStreamAsync(Stream underlyingStream)
{
    Stream substream = underlyingStream.ReadSubstream();
    await o1.DeserializeAsync(substream);

    substream = underlyingStream.ReadSubstream();
    await o2.DeserializeAsync(substream);
}
```
