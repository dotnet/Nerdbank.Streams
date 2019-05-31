# `Sequence<T>`

`Sequence<T>` is a builder for `ReadOnlySequence<T>`, which was popularized by the [System.IO.Pipelines API][Pipelines].
It can be used both to build and maintain sequences as they are read.

A `Sequence<T>` efficiently uses larger buffers to store many smaller writes. When a part of the sequence has been read, the buffers backing the read data are recycled for use later. This allows for a virtually alloc-free cycle of reading and writing.

## Sample

The following sample demonstrates creating a sequence, and responding to reads by advancing to the position read.

``` cs --region methods --source-file .\SampleProject\Program.cs --project .\SampleProject\SampleProject.csproj
```

[Pipelines]: https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/
