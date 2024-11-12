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

    [Fact]
    public void SequenceEqual()
    {
        ReadOnlySequence<byte> empty1 = default;
        Assert.True(empty1.SequenceEqual(empty1));

        Sequence<byte> shortContiguousSequence = new();
        shortContiguousSequence.Write((Span<byte>)[1, 2]);
        Assert.False(empty1.Equals(shortContiguousSequence.AsReadOnlySequence));
        Assert.False(shortContiguousSequence.AsReadOnlySequence.SequenceEqual(empty1));

        Sequence<byte> fragmentedSequence1 = new();
        fragmentedSequence1.Append(new byte[] { 1, 2 });
        fragmentedSequence1.Append(new byte[] { 3, 4, 5 });
        Assert.False(shortContiguousSequence.AsReadOnlySequence.SequenceEqual(fragmentedSequence1));
        Assert.False(empty1.SequenceEqual(fragmentedSequence1));

        Sequence<byte> fragmentedSequence2 = new();
        fragmentedSequence2.Append(new byte[] { 1, 2, 3 });
        fragmentedSequence2.Append(new byte[] { 4, 5 });
        Assert.True(fragmentedSequence1.AsReadOnlySequence.SequenceEqual(fragmentedSequence2));
    }
}
