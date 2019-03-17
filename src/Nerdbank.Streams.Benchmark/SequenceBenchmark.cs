// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Text;
    using BenchmarkDotNet.Attributes;

    public class SequenceBenchmark
    {
        [Benchmark]
        public void OneSegment_GetMemory()
        {
            using (var sequence = new Sequence<byte>())
            {
                var mem = sequence.GetMemory(12);
                sequence.Advance(4);
                mem = sequence.GetMemory(8);
                sequence.Advance(4);
                var ros = sequence.AsReadOnlySequence;
            }
        }

        [Benchmark]
        public void OneSegment_GetSpan()
        {
            using (var sequence = new Sequence<byte>())
            {
                var span = sequence.GetSpan(12);
                sequence.Advance(4);
                span = sequence.GetSpan(8);
                sequence.Advance(4);
                var ros = sequence.AsReadOnlySequence;
            }
        }

        [Benchmark]
        public void MultiSegment_GetMemory()
        {
            using (var sequence = new Sequence<byte>())
            {
                var mem = sequence.GetMemory(12);
                sequence.Advance(10);
                mem = sequence.GetMemory(128);
                sequence.Advance(120);
                mem = sequence.GetMemory(256);
                sequence.Advance(250);
                var ros = sequence.AsReadOnlySequence;
            }
        }

        [Benchmark]
        public void MultiSegment_GetSpan()
        {
            using (var sequence = new Sequence<byte>())
            {
                var span = sequence.GetSpan(12);
                sequence.Advance(10);
                span = sequence.GetSpan(128);
                sequence.Advance(120);
                span = sequence.GetSpan(256);
                sequence.Advance(250);
                var ros = sequence.AsReadOnlySequence;
            }
        }
    }
}
