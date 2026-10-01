# Security considerations

Nerdbank.Streams exposes transport primitives. It does not authenticate peers, authorize operations, encrypt bytes, or validate application messages. Perform authentication and authorization on the connection **before** attaching a higher-level protocol such as JSON-RPC, and protect traffic with TLS when crossing a network. JSON-RPC itself does not authenticate callers.

## Shared memory

<xref:Nerdbank.Streams.SharedMemoryDuplexPipe> is for mutually trusted processes running as the same user, not a boundary between different privilege levels. It restricts the rendezvous to the current user on .NET 8+; on .NET Framework the listener applies a current-user-only ACL and clients verify the listener's owner SID. Netstandard assemblies reject the transport because they cannot enforce the same checks. Windows mappings and named events use the creating process's default security in the current logon session; Unix backing files use owner-only (0600) permissions and are unlinked after mapping.

Another process **running as the same user** that learns the channel name can race to connect first. Generate an unpredictable name and give it only to the intended peer through a trusted path. The listener creates a new mapping rather than adopting an existing name, preventing an attacker from pre-creating its contents. The endpoint verifies the rendezvous peer is the current user, not a particular process or application.

Both endpoints can change mapped bytes at any time, even while the other reads them. A malicious or compromised peer can alter messages during deserialization, corrupt ring bookkeeping, or deny service. All library memory access remains within the mapping, but an application must still treat received content as untrusted input and enforce message-size and resource limits in its higher-level protocol. Prefer a conventional pipe or socket for untrusted peers, especially across privilege levels.
