// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class SequenceTests : TestBase
{
    public SequenceTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void Empty()
    {
        var seq = new Sequence<byte>();
        ReadOnlySequence<byte> ros = seq;
        Assert.True(ros.IsEmpty);
    }

    [Fact]
    public void GetMemory_Sizes()
    {
        var seq = new Sequence<char>();

        var mem1 = seq.GetMemory(16);
        Assert.Equal(16, mem1.Length);

        var mem2 = seq.GetMemory(32);
        Assert.Equal(32, mem2.Length);

        var mem3 = seq.GetMemory(0);
        Assert.NotEqual(0, mem3.Length);

        Assert.Throws<ArgumentOutOfRangeException>(() => seq.GetMemory(-1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void GetMemory_TwiceInARowRecyclesOldArray(int leadingBlocks)
    {
        MockPool<char> mockPool = new MockPool<char>();
        var seq = new Sequence<char>(mockPool);

        for (int i = 0; i < leadingBlocks; i++)
        {
            seq.GetMemory(1);
            seq.Advance(1);
        }

        var mem1 = seq.GetMemory(16);

        // This second request cannot be satisfied by the first one since it's larger. So the first should be freed.
        var mem2 = seq.GetMemory(32);
        mockPool.AssertContents(mem1);

        // This third one *can* be satisfied by the 32 byte array allocation requested previously, so no recycling should take place.
        var mem3 = seq.GetMemory(24);
        mockPool.AssertContents(mem1);
    }

    [Fact]
    public void Advance_OneBlock()
    {
        var seq = new Sequence<char>();
        var mem1 = seq.GetMemory(3);
        mem1.Span[0] = 'a';
        mem1.Span[1] = 'b';
        Assert.True(seq.AsReadOnlySequence.IsEmpty);
        seq.Advance(2);
        Assert.Equal("ab".ToCharArray(), seq.AsReadOnlySequence.ToArray());
    }

    [Fact]
    public void Advance_TwoBlocks_Advance()
    {
        var seq = new Sequence<char>();

        var mem1 = seq.GetMemory(3);
        mem1.Span[0] = 'a';
        mem1.Span[1] = 'b';
        seq.Advance(2);

        var mem2 = seq.GetMemory(2);
        mem2.Span[0] = 'c';
        mem2.Span[1] = 'd';
        seq.Advance(2);

        Assert.Equal("abcd".ToCharArray(), seq.AsReadOnlySequence.ToArray());

        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(1));
        Assert.Equal("bcd".ToCharArray(), seq.AsReadOnlySequence.ToArray());
        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(2));
        Assert.Equal("d".ToCharArray(), seq.AsReadOnlySequence.ToArray());
    }

    [Fact]
    public void Advance_EmptyBlock()
    {
        var seq = new Sequence<char>();
        var mem1 = seq.GetMemory(3);
        seq.Advance(0);

        Assert.True(seq.AsReadOnlySequence.IsEmpty);
    }

    [Fact]
    public void Advance_InvalidArgs()
    {
        var seq = new Sequence<char>();
        var mem1 = seq.GetMemory(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Advance(-1));
    }

    [Fact]
    public void Advance_TooFar()
    {
        var seq = new Sequence<char>();
        var mem1 = seq.GetMemory(3);
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Advance(mem1.Length + 1));
    }

    [Fact]
    public void AdvanceTo_ReturnsArraysToPool()
    {
        MockPool<char> mockPool = new MockPool<char>();
        var seq = new Sequence<char>(mockPool);

        var mem1 = seq.GetMemory(3);
        mem1.Span.Fill('a');
        seq.Advance(mem1.Length);

        var mem2 = seq.GetMemory(3);
        mem2.Span.Fill('b');
        seq.Advance(mem2.Length);

        var mem3 = seq.GetMemory(3);
        mem3.Span.Fill('c');
        seq.Advance(mem3.Length);

        // Assert that the used arrays are not in the pool.
        Assert.Empty(mockPool.Contents);

        // Advance, but don't go beyond the first array.
        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(mem1.Length - 1));
        Assert.Empty(mockPool.Contents);

        // Now advance beyond the first array and assert that it has been returned to the pool.
        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(1));
        mockPool.AssertContents(mem1);

        // Skip past the second array.
        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(mem2.Length));
        mockPool.AssertContents(mem1, mem2);

        // Advance part way through the third array.
        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(mem3.Length - 2));
        mockPool.AssertContents(mem1, mem2);

        // Now advance to the end.
        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(2));
        Assert.True(seq.AsReadOnlySequence.IsEmpty);
        mockPool.AssertContents(mem1, mem2, mem3);
    }

    [Fact]
    public void AdvanceTo_PriorPositionWithinBlock()
    {
        MockPool<char> mockPool = new MockPool<char>();
        var seq = new Sequence<char>(mockPool);

        var mem1 = seq.GetMemory(3);
        mem1.Span.Fill('a');
        seq.Advance(mem1.Length);

        var mem2 = seq.GetMemory(3);
        mem2.Span.Fill('b');
        seq.Advance(mem2.Length);

        ReadOnlySequence<char> ros = seq;
        SequencePosition pos1 = ros.GetPosition(1);
        SequencePosition pos2 = ros.GetPosition(2);

        seq.AdvanceTo(pos2);
        Assert.Throws<ArgumentException>(() => seq.AdvanceTo(pos1));
        ros = seq;
        Assert.Equal(4, ros.Length);
    }

    [Fact]
    public void AdvanceTo_PriorPositionInPriorBlock()
    {
        MockPool<char> mockPool = new MockPool<char>();
        var seq = new Sequence<char>(mockPool);

        var mem1 = seq.GetMemory(3);
        mem1.Span.Fill('a');
        seq.Advance(mem1.Length);

        var mem2 = seq.GetMemory(3);
        mem2.Span.Fill('b');
        seq.Advance(mem2.Length);

        ReadOnlySequence<char> ros = seq;
        SequencePosition pos1 = ros.GetPosition(1);
        SequencePosition pos4 = ros.GetPosition(4);

        seq.AdvanceTo(pos4);
        Assert.Throws<ArgumentException>(() => seq.AdvanceTo(pos1));
        ros = seq;
        Assert.Equal(2, ros.Length);
        Assert.Equal(ros.Length, seq.Length);
    }

    [Fact]
    public void AdvanceTo_PositionFromUnrelatedSequence()
    {
        MockPool<char> mockPool = new MockPool<char>();
        var seqA = new Sequence<char>(mockPool);
        var seqB = new Sequence<char>(mockPool);

        var mem1 = seqA.GetMemory(3);
        mem1.Span.Fill('a');
        seqA.Advance(mem1.Length);

        var mem2 = seqB.GetMemory(3);
        mem2.Span.Fill('b');
        seqB.Advance(mem2.Length);

        ReadOnlySequence<char> rosA = seqA;
        ReadOnlySequence<char> rosB = seqB;

        var posB = rosB.GetPosition(2);
        Assert.Throws<ArgumentException>(() => seqA.AdvanceTo(posB));
        Assert.Equal(3, seqA.AsReadOnlySequence.Length);
        Assert.Equal(3, seqB.AsReadOnlySequence.Length);
    }

    [Fact]
    public void AdvanceTo_LaterPositionInCurrentBlock()
    {
        ReadOnlySpan<char> original = "abcdefg".ToCharArray();
        var seq = new Sequence<char>();
        seq.Write(original);
        var ros = seq.AsReadOnlySequence;

        seq.AdvanceTo(ros.GetPosition(5, ros.Start));
        ros = seq.AsReadOnlySequence;
        Assert.Equal<char>(original.Slice(5).ToArray(), ros.First.ToArray());

        seq.AdvanceTo(ros.GetPosition(2, ros.Start));
        Assert.Equal(0, seq.AsReadOnlySequence.Length);
    }

    [Fact]
    public void AdvanceTo_InterweavedWith_Advance()
    {
        ReadOnlySpan<char> original = "abcdefg".ToCharArray();
        ReadOnlySpan<char> later = "hijkl".ToCharArray();
        var seq = new Sequence<char>();
        var mem = seq.GetMemory(30); // Specify a size with enough space to store both buffers
        original.CopyTo(mem.Span);
        seq.Advance(original.Length);

        var originalRos = seq.AsReadOnlySequence;
        var origLastCharPosition = originalRos.GetPosition(originalRos.Length - 1);
        char origLastChar = originalRos.Slice(origLastCharPosition, 1).First.Span[0];

        // "Consume" a few characters, but leave the origEnd an unconsumed position so it should be valid.
        seq.AdvanceTo(originalRos.GetPosition(3, originalRos.Start));

        // Verify that the SequencePosition we saved before still represents the same character.
        Assert.Equal(origLastChar, seq.AsReadOnlySequence.Slice(origLastCharPosition, 1).First.Span[0]);

        // Append several characters
        mem = seq.GetMemory(later.Length);
        later.CopyTo(mem.Span);
        seq.Advance(later.Length);

        // Verify that the SequencePosition we saved before still represents the same character.
        Assert.Equal(origLastChar, seq.AsReadOnlySequence.Slice(origLastCharPosition, 1).First.Span[0]);
    }

    [Fact]
    public void AdvanceTo_InterweavedWith_Advance2()
    {
        // use the mock pool so that we can predict the actual array size will not exceed what we ask for.
        var seq = new Sequence<int>(new MockPool<int>());

        seq.GetSpan(10);
        seq.Advance(10);

        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(3));

        seq.GetSpan(10);
        seq.Advance(10);

        seq.GetSpan(10);
        seq.Advance(10);

        Assert.Equal(10 - 3 + 10 + 10, seq.AsReadOnlySequence.Length);
    }

    [Fact]
    public void Dispose_ReturnsArraysToPool()
    {
        MockPool<char> mockPool = new MockPool<char>();
        var seq = new Sequence<char>(mockPool);
        var expected = new List<Memory<char>>();
        for (int i = 0; i < 3; i++)
        {
            var mem = seq.GetMemory(3);
            expected.Add(mem);
            seq.Advance(mem.Length);
        }

        seq.Dispose();
        Assert.True(seq.AsReadOnlySequence.IsEmpty);
        mockPool.AssertContents(expected);
    }

    [Fact]
    public void Dispose_CanHappenTwice()
    {
        var seq = new Sequence<char>();
        seq.Write(new char[3]);
        seq.Dispose();
        seq.Dispose();
    }

    [Fact]
    public void Dispose_ClearsAndAllowsReuse()
    {
        var seq = new Sequence<char>();
        seq.Write(new char[3]);
        seq.Dispose();
        Assert.True(seq.AsReadOnlySequence.IsEmpty);
        seq.Write(new char[3]);
        Assert.Equal(3, seq.AsReadOnlySequence.Length);
    }

    private class MockPool<T> : MemoryPool<T>
    {
        public override int MaxBufferSize => throw new NotImplementedException();

        public List<IMemoryOwner<T>> Contents { get; } = new List<IMemoryOwner<T>>();

        public override IMemoryOwner<T> Rent(int minBufferSize = -1)
        {
            IMemoryOwner<T> result = null;
            if (minBufferSize <= 0)
            {
                result = this.Contents.FirstOrDefault();
            }
            else
            {
                result = this.Contents.FirstOrDefault(a => a.Memory.Length >= minBufferSize);
            }

            if (result == null)
            {
                result = new Rental(this, new T[minBufferSize]);
            }
            else
            {
                this.Contents.Remove(result);
            }

            return result;
        }

        internal void AssertContents(params Memory<T>[] expectedArrays) => this.AssertContents((IEnumerable<Memory<T>>)expectedArrays);

        internal void AssertContents(IEnumerable<Memory<T>> expectedArrays)
        {
            Assert.Equal(expectedArrays, this.Contents.Select(c => c.Memory));
        }

        protected override void Dispose(bool disposing)
        {
        }

        private void Return(Rental rental)
        {
            this.Contents.Add(rental);
        }

        private class Rental : IMemoryOwner<T>
        {
            private readonly MockPool<T> owner;

            internal Rental(MockPool<T> owner, Memory<T> memory)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                this.Memory = memory;
            }

            public Memory<T> Memory { get; }

            public void Dispose()
            {
                this.owner.Return(this);
            }
        }
    }
}
