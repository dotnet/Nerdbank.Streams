// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.Runtime.InteropServices;
using Nerdbank.Streams;
using Xunit;

public class ReadOnlySequenceExtensionsTests
{
    [Fact]
    public void Clone()
    {
        int[] array = new[] { 1, 2, 3 };
        Sequence<int> seq = new();
        seq.Append(array);

        ReadOnlySequence<int> copy = seq.AsReadOnlySequence.Clone();
        Assert.Equal(array, copy.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(seq.AsReadOnlySequence.First, out ArraySegment<int> seqFirstSegment));
        Assert.True(MemoryMarshal.TryGetArray(copy.First, out ArraySegment<int> copyFirstSegment));
        Assert.NotSame(seqFirstSegment.Array, copyFirstSegment.Array);
    }
}
