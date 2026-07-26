# Monitoring Stream

The `MonitoringStream` class wraps another `Stream` and raises events when any I/O operation is performed on it.
This allows you to monitor and/or trace the operations including the data actually being sent or received.

Events for reading, writing, seeking, truncating, and disposal are offered.
For most of these events, both before and after events are offered.

Any exceptions thrown by the event handlers will propagate to the caller.

## Read logging sample

Here is a method that uses `MonitoringStream` to monitor all reads from another Stream:

```cs
Stream LogReads(Stream underlyingStream)
{
    Stream log = File.Open($"some.reads.log", FileMode.Create, FileAccess.Write, FileShare.Read);

    var monitoringStream = new MonitoringStream(underlyingStream);
    monitoringStream.DidRead += (s, e) =>
    {
        log.Write(e.Array, e.Offset, e.Count);
        log.Flush();
    };
    monitoringStream.DidReadByte += (s, e) =>
    {
        if (e >= 0)
        {
            log.WriteByte((byte)e);
            log.Flush();
        }
    };
    monitoringStream.Disposed += (s, e) =>
    {
        log.Dispose();
    };

    return monitoringStream;
}
```

At this point, when you have a `Stream` that you'll read from, but you want to record all bytes you read to a file,
you can pass that `Stream` to the method above and then perform your reads on the result `Stream`:

```cs
byte[] buffer = new byte[1024];
Stream underlyingStream; // acquisition out of scope for this doc
var monitoredStream = LogReads(underlyingStream);
monitoredStream.Read(buffer, 0, buffer.Length);
```

After this code runs, the bytes read will be both in `buffer` and in the `some.reads.log` file.
