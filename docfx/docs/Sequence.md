# `Sequence<T>`

`Sequence<T>` is a builder for `ReadOnlySequence<T>`, which was popularized by the [System.IO.Pipelines API][Pipelines].
It can be used both to build and maintain sequences as they are read.

A `Sequence<T>` efficiently uses larger buffers to store many smaller writes. When a part of the sequence has been read, the buffers backing the read data are recycled for use later. This allows for a virtually alloc-free cycle of reading and writing.

## Sample

The following sample demonstrates creating a sequence, and responding to reads by advancing to the position read.

```cs
var seq = new Sequence<char>();

var mem1 = seq.GetMemory(3);
mem1.Span[0] = 'a';
mem1.Span[1] = 'b';
seq.Advance(2);

var mem2 = seq.GetMemory(2);
mem2.Span[0] = 'c';
mem2.Span[1] = 'd';
seq.Advance(2);

ReadOnlySequence<T> ros = seq.AsReadOnlySequence;
Assert.Equal("abcd".ToCharArray(), ros.ToArray());

seq.AdvanceTo(ros.GetPosition(1));
ros = seq.AsReadOnlySequence;
Assert.Equal("bcd".ToCharArray(), ros.ToArray());
```

[Pipelines]: https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/
