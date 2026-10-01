# Shared-memory duplex pipe

<xref:Nerdbank.Streams.SharedMemoryDuplexPipe> is an <xref:System.IO.Pipelines.IDuplexPipe> for two trusted processes on the same machine. Producers write into mapped memory and consumers read from that mapping without a kernel copy. This is a byte transport, not a messaging protocol: layer your own framing (for example, a length prefix) and serializer on top.

## When to use it

Shared memory is useful for large messages or sustained local traffic, where removing copies can outweigh signaling overhead. On Windows it uses named events to wake a waiting peer; elsewhere it uses a named-pipe doorbell. It does **not** busy-wait. For small, infrequent messages, ordinary named pipes are simpler and may be as fast or faster. Unlike a socket, shared memory cannot connect hosts on different machines. A channel name identifies exactly one two-party connection, not a multi-client listener; negotiate distinct names for multiple clients.

The peer must use the same shared-memory layout and the same <xref:Nerdbank.Streams.SharedMemoryPipeOptions>. This transport is compatible with other protocols that use `IDuplexPipe`, but not with arbitrary shared-memory implementations. Each end must use a version of Nerdbank.Streams that understands the same layout; there is no protocol-version negotiation. See [Security considerations](security.md) before passing the channel name to another process.

## Connecting

Create an unguessable name and convey it through a trusted path. Start the listener before connecting. Both calls return endpoints that you must dispose:

```csharp
using System.IO.Pipelines;
using Nerdbank.Streams;

string name = Guid.NewGuid().ToString("N");
Task<SharedMemoryDuplexPipe> listening = SharedMemoryDuplexPipe.ListenAsync(name);
SharedMemoryDuplexPipe client = await SharedMemoryDuplexPipe.ConnectAsync(name);
SharedMemoryDuplexPipe server = await listening;
using (client)
using (server)
{
    await client.Output.WriteAsync(new byte[] { 1, 2, 3 });
    ReadResult read = await server.Input.ReadAsync();
    server.Input.AdvanceTo(read.Buffer.End);
}
```

In a real application the listener and client usually run in different processes. <xref:Nerdbank.Streams.SharedMemoryDuplexPipe.CreatePairAsync*> creates endpoints in one process for testing; unlike listen/connect, it uses in-process wake-ups. When the peer exits or disposes its endpoint, the reader completes.

## Capacity and platform support

<xref:Nerdbank.Streams.SharedMemoryPipeOptions.Capacity> defaults to 1 MiB **per direction** and must be a power of two. It bounds the amount of unconsumed data, *not* the total message size: when a serializer needs more space, an oversized message is staged in pooled private memory and copied into the ring as the reader advances. A full ring causes the writer's `FlushAsync` to wait asynchronously. Set the capacity large enough to fit typical writes and the in-flight data you expect. The mapping reserves approximately twice the capacity per connection; its pages are touched only as needed, and the writer returns to the beginning after the reader catches up.

<xref:Nerdbank.Streams.SharedMemoryPipeOptions.Signaling> defaults to named events on Windows and a named-pipe doorbell on Linux/macOS. <xref:Nerdbank.Streams.SharedMemoryPipeOptions.BaseDirectory> selects the Unix backing-file directory (normally `/dev/shm` where available). Windows uses a paging-file-backed mapping and creates no file. On Unix, the backing file is owner-only and is unlinked after the client maps it; if setup fails, the listener cleans it up.

The .NET 8+ assembly supports this feature on Windows and Unix. The .NET Framework 4.7.2 assembly supports it on Windows, using a current-user-only rendezvous pipe ACL and verifying the listener's owner SID. The `netstandard2.0` and `netstandard2.1` assemblies reject all three factory methods with <xref:System.PlatformNotSupportedException> because they cannot guarantee these peer-identity checks. Deploy the .NET Framework or .NET 8+ asset instead.
