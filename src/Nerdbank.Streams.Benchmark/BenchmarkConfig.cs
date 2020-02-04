// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using BenchmarkDotNet.Configs;
    using BenchmarkDotNet.Diagnosers;
    using BenchmarkDotNet.Jobs;

    internal class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            this.Add(DefaultConfig.Instance.With(MemoryDiagnoser.Default));
        }
    }
}
