# Multiplexing Stream

The `MultiplexingStream` class allows for any bidirectional .NET `Stream` to
represent many "channels" of communication. Each channel may carry a different protocol
(or simply carry binary data) and is efficiently carried over the stream with no
encoding/escaping applied.

The API for this class is still under development.
