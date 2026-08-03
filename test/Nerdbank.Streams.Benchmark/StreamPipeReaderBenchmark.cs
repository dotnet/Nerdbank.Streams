// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark;

using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

/// <summary>
/// Benchmarks asynchronous reads performed by <see cref="StreamPipeReader"/>.
/// </summary>
[Config(typeof(BenchmarkConfig))]
public class StreamPipeReaderBenchmark
{
    private const int ReadsPerInvocation = 100;

    private readonly MemoryStream stream = new MemoryStream(new byte[ReadsPerInvocation]);
    private readonly StreamPipeReader reader;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamPipeReaderBenchmark"/> class.
    /// </summary>
    public StreamPipeReaderBenchmark()
    {
        this.reader = new StreamPipeReader(this.stream, bufferSize: 1, leaveOpen: true);
    }

    /// <summary>
    /// Measures repeated asynchronous reads with a non-cancellable caller token.
    /// </summary>
    /// <returns>A task that completes when all reads finish.</returns>
    [Benchmark(OperationsPerInvoke = ReadsPerInvocation)]
    public async Task ReadAsyncWithCancellationTokenNone()
    {
        this.stream.Position = 0;
        for (int i = 0; i < ReadsPerInvocation; i++)
        {
            ReadResult result = await this.reader.ReadAsync(CancellationToken.None);
            this.reader.AdvanceTo(result.Buffer.End);
        }
    }
}
