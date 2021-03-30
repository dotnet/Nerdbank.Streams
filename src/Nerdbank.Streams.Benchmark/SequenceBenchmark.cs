// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Text;
    using BenchmarkDotNet.Attributes;

    [Config(typeof(BenchmarkConfig))]
    public sealed class SequenceBenchmark : IDisposable
    {
        private readonly Sequence<byte> sequence = new Sequence<byte>();

        [Benchmark]
        public void OneSegment_GetMemory()
        {
            var mem = this.sequence.GetMemory(12);
            this.sequence.Advance(4);
            mem = this.sequence.GetMemory(8);
            this.sequence.Advance(4);
            var ros = this.sequence.AsReadOnlySequence;
            this.sequence.Reset();
        }

        [Benchmark]
        public void OneSegment_GetSpan()
        {
            var span = this.sequence.GetSpan(12);
            this.sequence.Advance(4);
            span = this.sequence.GetSpan(8);
            this.sequence.Advance(4);
            var ros = this.sequence.AsReadOnlySequence;
            this.sequence.Reset();
        }

        [Benchmark]
        public void MultiSegment_GetMemory()
        {
            var mem = this.sequence.GetMemory(12);
            this.sequence.Advance(10);
            mem = this.sequence.GetMemory(128);
            this.sequence.Advance(120);
            mem = this.sequence.GetMemory(256);
            this.sequence.Advance(250);
            var ros = this.sequence.AsReadOnlySequence;
            this.sequence.Reset();
        }

        [Benchmark]
        public void MultiSegment_GetSpan()
        {
            var span = this.sequence.GetSpan(12);
            this.sequence.Advance(10);
            span = this.sequence.GetSpan(128);
            this.sequence.Advance(120);
            span = this.sequence.GetSpan(256);
            this.sequence.Advance(250);
            var ros = this.sequence.AsReadOnlySequence;
            this.sequence.Reset();
        }

        public void Dispose()
        {
            this.sequence.Dispose();
        }
    }
}
