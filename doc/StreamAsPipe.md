# Access streams using Pipelines APIs

The `Stream.UsePipeReader()` and `Stream.UsePipeWriter()` extension methods on a `Stream` facilitates
reading and writing to a stream using modern and efficient new pipeline APIs. 

Using these APIs isn't automatically better than the classic `Stream.ReadAsync` and `Stream.WriteAsync` methods,
but can be very useful when writing code that interops with native memory or other pipeline APIs in order
to avoid unnecessary buffer copies.
