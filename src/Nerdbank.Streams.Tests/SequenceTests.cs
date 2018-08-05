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
    public void Acquire_Sizes()
    {
        var seq = new Sequence<char>();

        var mem1 = seq.GetMemory(16);
        Assert.Equal(16, mem1.Memory.Length);

        var mem2 = seq.GetMemory(32);
        Assert.Equal(32, mem2.Memory.Length);

        Assert.Throws<ArgumentOutOfRangeException>(() => seq.GetMemory(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.GetMemory(0));
    }

    [Fact]
    public void Append_OneBlock()
    {
        var seq = new Sequence<char>();
        var mem1 = seq.GetMemory(3);
        mem1.Memory.Span[0] = 'a';
        mem1.Memory.Span[1] = 'b';
        seq.Append(mem1, 0, 2);
        Assert.Equal("ab".ToCharArray(), seq.AsReadOnlySequence().ToArray());
    }

    [Fact]
    public void Append_TwoBlocks_Advance()
    {
        var seq = new Sequence<char>();

        var mem1 = seq.GetMemory(3);
        mem1.Memory.Span[0] = 'a';
        mem1.Memory.Span[1] = 'b';
        seq.Append(mem1, 0, 2);

        var mem2 = seq.GetMemory(2);
        mem2.Memory.Span[0] = 'c';
        mem2.Memory.Span[1] = 'd';
        seq.Append(mem2, 0, 2);

        Assert.Equal("abcd".ToCharArray(), seq.AsReadOnlySequence().ToArray());

        seq.Advance(seq.AsReadOnlySequence().GetPosition(1));
        Assert.Equal("bcd".ToCharArray(), seq.AsReadOnlySequence().ToArray());
        seq.Advance(seq.AsReadOnlySequence().GetPosition(2));
        Assert.Equal("d".ToCharArray(), seq.AsReadOnlySequence().ToArray());
    }

    [Fact]
    public void Append_EmptyBlock()
    {
        var seq = new Sequence<char>();
        var mem1 = seq.GetMemory(3);
        seq.Append(mem1, 0, 0);

        var mem2 = seq.GetMemory(3);
        seq.Append(mem2, mem2.Memory.Length, 0);

        Assert.True(seq.AsReadOnlySequence().IsEmpty);
    }

    [Fact]
    public void Append_InvalidArgs()
    {
        var seq = new Sequence<char>();
        var mem1 = seq.GetMemory(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Append(mem1, -1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Append(mem1, 0, mem1.Memory.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Append(mem1, 2, mem1.Memory.Length - 1));

        Assert.Throws<ArgumentNullException>(() => seq.Append(null));
        Assert.Throws<ArgumentNullException>(() => seq.Append(null, 1));
        Assert.Throws<ArgumentNullException>(() => seq.Append(null, 0, 3));
    }

    [Fact]
    public void Advance_ReturnsArraysToPool()
    {
        MockPool<char> mockPool = new MockPool<char>();
        var seq = new Sequence<char>(mockPool);

        var mem1 = seq.GetMemory(3);
        mem1.Memory.Span.Fill('a');
        seq.Append(mem1, 0, mem1.Memory.Length);

        var mem2 = seq.GetMemory(3);
        mem2.Memory.Span.Fill('b');
        seq.Append(mem2, 0, mem2.Memory.Length);

        var mem3 = seq.GetMemory(3);
        mem3.Memory.Span.Fill('c');
        seq.Append(mem3, 0, mem3.Memory.Length);

        // Assert that the used arrays are not in the pool.
        Assert.Empty(mockPool.Contents);

        // Advance, but don't go beyond the first array.
        seq.Advance(seq.AsReadOnlySequence().GetPosition(mem1.Memory.Length - 1));
        Assert.Empty(mockPool.Contents);

        // Now advance beyond the first array and assert that it has been returned to the pool.
        seq.Advance(seq.AsReadOnlySequence().GetPosition(1));
        Assert.Equal(new[] { mem1 }, mockPool.Contents);

        // Skip past the second array.
        seq.Advance(seq.AsReadOnlySequence().GetPosition(mem2.Memory.Length));
        Assert.Equal(new[] { mem1, mem2 }, mockPool.Contents);

        // Advance part way through the third array.
        seq.Advance(seq.AsReadOnlySequence().GetPosition(mem3.Memory.Length - 1));
        Assert.Equal(new[] { mem1, mem2 }, mockPool.Contents);

        // Now advance to the end.
        seq.Advance(seq.AsReadOnlySequence().GetPosition(1));
        Assert.True(seq.AsReadOnlySequence().IsEmpty);
        Assert.Equal(new[] { mem1, mem2, mem3 }, mockPool.Contents);
    }

    [Fact]
    public void Reset_ReturnsArraysToPool()
    {
        MockPool<char> mockPool = new MockPool<char>();
        var seq = new Sequence<char>(mockPool);
        var expected = new List<IMemoryOwner<char>>();
        for (int i = 0; i < 3; i++)
        {
            var mem = seq.GetMemory(3);
            expected.Add(mem);
            seq.Append(mem, 0, mem.Memory.Length);
        }

        seq.Reset();
        Assert.True(seq.AsReadOnlySequence().IsEmpty);
        Assert.Equal(expected, mockPool.Contents);
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
