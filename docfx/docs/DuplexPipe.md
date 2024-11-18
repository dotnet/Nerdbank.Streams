# DuplexPipe

The `DuplexPipe` class is an implementation of .NET's `IDuplexPipe` interface.
When only a reader or only a writer are provided, the missing direction is filled in with a "completed" instance
so it can still fulfill an `IDuplexPipe` interface yet be discoverable as one where only one direction is allowed.

## Example

```cs
PipeReader reader;
PipeWriter writer;
var duplexPipe = new DuplexPipe(reader, writer);
```
