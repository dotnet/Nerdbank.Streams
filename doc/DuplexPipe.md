# DuplexPipe

The `DuplexPipe` class is a trivial implementation of .NET's `IDuplexPipe` interface.

## Example

```cs
PipeReader reader;
PipeWriter writer;
var duplexPipe = new DuplexPipe(reader, writer);
```
