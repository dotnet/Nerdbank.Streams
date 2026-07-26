// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using BenchmarkDotNet.Configs;
    using BenchmarkDotNet.Diagnosers;
    using BenchmarkDotNet.Engines;
    using BenchmarkDotNet.Jobs;
    using BenchmarkDotNet.Toolchains.InProcess.Emit;

    /// <summary>
    /// A configuration suited to long-running I/O benchmarks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default engine calibrates by invoking the benchmark many times, which is appropriate for
    /// microbenchmarks but wasteful when a single operation already runs for many milliseconds and
    /// performs real I/O. <see cref="RunStrategy.Monitoring"/> instead runs one operation per iteration,
    /// which keeps these benchmarks to a practical duration while still producing a mean and confidence
    /// interval over enough iterations to detect meaningful changes.
    /// </para>
    /// <para>
    /// These benchmarks also run in-process. The default toolchain generates and builds a separate project
    /// per benchmark, which is fragile when the installed SDK is newer than the target framework, and buys
    /// little here: an operation that runs for tens of milliseconds and is dominated by socket I/O is not
    /// meaningfully perturbed by sharing a process with the host.
    /// </para>
    /// </remarks>
    internal class MultiplexingStreamBenchmarkConfig : ManualConfig
    {
        public MultiplexingStreamBenchmarkConfig()
        {
            this.Add(DefaultConfig.Instance
                .AddDiagnoser(MemoryDiagnoser.Default)
                .AddJob(Job.Default
                    .WithToolchain(InProcessEmitToolchain.Instance)
                    .WithStrategy(RunStrategy.Monitoring)
                    .WithInvocationCount(1)
                    .WithUnrollFactor(1)
                    .WithWarmupCount(3)
                    .WithIterationCount(15)));
        }
    }
}
