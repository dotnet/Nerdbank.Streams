# Access existing I/O APIS using Pipelines

The `UsePipeReader()`, `UsePipeWriter()`, `UsePipe()` extension methods are offered on the following types:

1. `Stream`
2. `WebSocket`

By using these extension methods on these types, one can use efficient `PipeReader` and `PipeWriter` classes to perform I/O operations
and connect these transports with APIs that use these.

Using these APIs isn't automatically better than the classic `Stream.ReadAsync` and `Stream.WriteAsync` methods,
but can be very useful when writing code that interops with native memory or other pipeline APIs in order
to avoid unnecessary buffer copies.

One can use these apis on `System.Net.Sockets.Socket`, by wrapping it in a `System.Net.Sockets.NetworkStream`

```csharp
// 'Socket' has already been created and connected.
var networkStream = new NetworkStream(Socket, ownsSocket: false);
IDuplexPipe pipe = networkStream.UsePipe(cancellationToken: DisposedToken);
```
