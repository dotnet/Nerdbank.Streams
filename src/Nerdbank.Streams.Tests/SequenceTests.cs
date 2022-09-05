// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft;
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
        var seq = new Sequence<char>(new MockMemoryPool<char>());
        seq.MinimumSpanLength = 1;

        var mem1 = seq.GetMemory(16);
        Assert.Equal(16, mem1.Length);

        var mem2 = seq.GetMemory(32);
        Assert.Equal(32, mem2.Length);

        var mem3 = seq.GetMemory(0);
        Assert.NotEqual(0, mem3.Length);

        Assert.Throws<ArgumentOutOfRangeException>(() => seq.GetMemory(-1));
    }

    [Fact]
    public void Reset_AfterPartialAdvance()
    {
        var seq = new Sequence<object>(new MockMemoryPool<object> { Contents = { new object[4] } });
        seq.Write(new object[3]);
        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(2));
        seq.Reset();
    }

    [Fact]
    public void MemoryPool_ReleasesReferenceOnRecycle()
    {
        var seq = new Sequence<object>(new MockMemoryPool<object>());
        var weakReference = StoreReferenceInSequence(seq);
        seq.Reset();
        GC.Collect();
        Assert.False(weakReference.IsAlive);
    }

    [Fact]
    public void ArrayPool_ReleasesReferenceOnRecycle()
    {
        var seq = new Sequence<object>(new MockArrayPool<object>());
        var weakReference = StoreReferenceInSequence(seq);
        seq.Reset();
        GC.Collect();
        Assert.False(weakReference.IsAlive);
    }

    [Fact]
    public void ArrayPool_ReleasesReferenceInStructsOnRecycle()
    {
        var seq = new Sequence<ValueTuple<object>>(new MockArrayPool<ValueTuple<object>>());
        var weakReference = StoreReferenceInSequence(seq);
        seq.Reset();
        GC.Collect();
        Assert.False(weakReference.IsAlive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void GetMemory_TwiceInARowRecyclesOldArray(int leadingBlocks)
    {
        MockMemoryPool<char> mockPool = new MockMemoryPool<char>();
        var seq = new Sequence<char>(mockPool);
        seq.MinimumSpanLength = 1;

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

    /// <summary>
    /// Verifies that folks can "reserve" space for a header, write content, then circle back and write
    /// the header later.
    /// </summary>
    /// <seealso href="https://github.com/dotnet/corefx/issues/34259"/>
    [Fact]
    public void GetSpan_ReservesHeaderSpaceForWritingLater()
    {
        var seq = new Sequence<char>();

        var headerSpan = seq.GetSpan(4);
        seq.Advance(4);

        var contentSpan = seq.GetSpan(10);
        "0123456789".AsSpan().CopyTo(contentSpan);
        seq.Advance(10);

        "abcd".AsSpan().CopyTo(headerSpan);

        Assert.Equal("abcd0123456789", new string(seq.AsReadOnlySequence.ToArray()));
    }

    [Fact]
    public void Advance_BeforeGetMemory()
    {
        var seq = new Sequence<char>();
        Assert.Throws<InvalidOperationException>(() => seq.Advance(1));
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
    public void AdvanceTo_EmptySequence()
    {
        using var seq = new Sequence<byte>();
        seq.AdvanceTo(seq.AsReadOnlySequence.Start);
        Assert.Equal(0, seq.Length);
    }

    [Fact]
    public void AdvanceTo_DefaultSequencePosition()
    {
        using var seq = new Sequence<byte>();

        // PipeReader.AdvanceTo(default) simply no-ops. We emulate that here.
        seq.AdvanceTo(default);
    }

    [Fact]
    public void AdvanceTo_ReturnsArraysToPool()
    {
        MockMemoryPool<char> mockPool = new MockMemoryPool<char>();
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
        MockMemoryPool<char> mockPool = new MockMemoryPool<char>();
        var seq = new Sequence<char>(mockPool);

        var mem1 = seq.GetMemory(3).Slice(0, 3);
        mem1.Span.Fill('a');
        seq.Advance(mem1.Length);

        var mem2 = seq.GetMemory(3).Slice(0, 3);
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
        MockMemoryPool<char> mockPool = new MockMemoryPool<char>();
        var seq = new Sequence<char>(mockPool);

        var mem1 = seq.GetMemory(3).Slice(0, 3);
        mem1.Span.Fill('a');
        seq.Advance(mem1.Length);

        var mem2 = seq.GetMemory(3).Slice(0, 3);
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
        MockMemoryPool<char> mockPool = new MockMemoryPool<char>();
        var seqA = new Sequence<char>(mockPool);
        var seqB = new Sequence<char>(mockPool);

        var mem1 = seqA.GetMemory(3).Slice(0, 3);
        mem1.Span.Fill('a');
        seqA.Advance(mem1.Length);

        var mem2 = seqB.GetMemory(3).Slice(0, 3);
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
        var seq = new Sequence<int>(new MockMemoryPool<int>());

        var span = seq.GetSpan(10);
        Enumerable.Range(1, 10).ToArray().CopyTo(span);
        seq.Advance(10);

        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(3));

        span = seq.GetSpan(10);
        Enumerable.Range(11, 10).ToArray().CopyTo(span);
        seq.Advance(10);

        span = seq.GetSpan(10);
        Enumerable.Range(21, 10).ToArray().CopyTo(span);
        seq.Advance(10);

        this.Logger.WriteLine(string.Join(", ", seq.AsReadOnlySequence.ToArray()));
        Assert.Equal(Enumerable.Range(4, 27), seq.AsReadOnlySequence.ToArray());
        Assert.Equal(10 - 3 + 10 + 10, seq.AsReadOnlySequence.Length);
    }

    [Fact]
    public void AdvanceTo_ReleasesReferences()
    {
        var seq = new Sequence<object>();

        WeakReference tracker = StoreReferenceInSequence(seq);

        GC.Collect();
        Assert.True(tracker.IsAlive);
        seq.AdvanceTo(seq.AsReadOnlySequence.GetPosition(1));
        GC.Collect();
        Assert.False(tracker.IsAlive);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(64)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void MinimumSpanLength(int minLength)
    {
        var seq = new Sequence<int>();
        Assert.Equal(0, seq.MinimumSpanLength);
        seq.MinimumSpanLength = minLength;
        Assert.Equal(minLength, seq.MinimumSpanLength);
        var span = seq.GetSpan(1);
        Assert.True(span.Length >= seq.MinimumSpanLength);

        seq.Reset();
        Assert.Equal(minLength, seq.MinimumSpanLength);
    }

    [Fact]
    public void MinimumSpanLength_DoesNotAllocateIfThereAreBytesLeft()
    {
        var pool = new MockMemoryPool<byte>();
        var seq = new Sequence<byte>(pool)
        {
            MinimumSpanLength = 64,
        };
        Span<byte> firstSpan = seq.GetSpan(1);
        seq.Advance(1);
        Span<byte> secondSpan = seq.GetSpan(63);
        seq.Advance(63);

        Assert.Equal(64, firstSpan.Length);
        Assert.Equal(63, secondSpan.Length);
        Assert.Equal(1, pool.RentCallCount);
    }

    [Fact]
    public void MinimumSpanLength_ZeroGetsPoolRecommendation()
    {
        var pool = new MockMemoryPool<int>();
        var seq = new Sequence<int>(pool);
        seq.MinimumSpanLength = 0;
        var span = seq.GetSpan(0);
        Assert.Equal(pool.DefaultLength, span.Length);
    }

    [Fact]
    public void Dispose_ReturnsArraysToPool_MemoryPool()
    {
        MockMemoryPool<char> mockPool = new MockMemoryPool<char>();
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
    public void Dispose_ReturnsArraysToPool_ArrayPool()
    {
        MockArrayPool<char> mockPool = new MockArrayPool<char>();
        var seq = new Sequence<char>(mockPool);
        var expected = new List<char[]>();
        for (int i = 0; i < 3; i++)
        {
            var mem = seq.GetMemory(3);
            Assumes.True(MemoryMarshal.TryGetArray<char>(mem, out var segment));
            expected.Add(segment.Array!);
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

    [Fact]
    public void Append()
    {
        var first = new int[] { 1, 2, 3 };
        var second = new int[] { 4, 5, 6 };
        var third = new int[] { 7, 8, 9 };

        // Arrange for an array pool that will definitely create slack space so we can verify it is handled properly.
        var arrayPool = new MockArrayPool<int> { MinArraySizeFactor = 2 };
        var seq = new Sequence<int>(arrayPool);

        first.CopyTo(seq.GetSpan(first.Length).Slice(0, first.Length));
        seq.Advance(first.Length);
        Assert.Equal(first.Length, seq.Length);

        seq.Append(second);
        Assert.Equal(first.Length + second.Length, seq.Length);

        third.CopyTo(seq.GetSpan(third.Length).Slice(0, third.Length));
        seq.Advance(third.Length);
        Assert.Equal(first.Length + second.Length + third.Length, seq.Length);

        var ros = seq.AsReadOnlySequence;
        Assert.Equal(Enumerable.Range(1, 9), ros.ToArray());

        // Verify that the second segment is the only one that has reference equality with the original buffer.
        int idx = 0;
        foreach (var segment in ros)
        {
            switch (idx++)
            {
                case 0:
                    Assert.NotEqual(first, segment);
                    break;
                case 1:
                    Assert.Equal(second, segment);
                    break;
                case 2:
                    Assert.NotEqual(third, segment);
                    break;
                default:
                    Assert.False(true);
                    break;
            }
        }

        // Verify that the appended array does NOT get returned to the array pool.
        seq.Reset();
        Assert.Equal(2, arrayPool.Contents.Count);
        Assert.DoesNotContain(second, arrayPool.Contents);
    }

    [Fact]
    public void Append_Empty()
    {
        var arrayPool = new MockArrayPool<int> { MinArraySizeFactor = 2 };
        var seq = new Sequence<int>(arrayPool);

        seq.Append(default);
        Assert.Equal(0, seq.Length);

        seq.Append(default);
        Assert.Equal(0, seq.Length);

        seq.Append(new int[] { 1, 2, 3 });
        Assert.Equal(3, seq.Length);

        seq.Append(default);
        Assert.Equal(3, seq.Length);

        Assert.Equal(new int[] { 1, 2, 3 }, seq.AsReadOnlySequence.ToArray());
    }

    [Fact]
    public void AutoIncreaseMinimumSpanLength_Default()
    {
        Assert.True(new Sequence<byte>().AutoIncreaseMinimumSpanLength);
    }

    [Fact]
    public void AutoIncreaseMinimumSpanLength_TrueBehavior()
    {
        var sequence = new Sequence<int>(new MockArrayPool<int>()) { AutoIncreaseMinimumSpanLength = true, MinimumSpanLength = 4 };
        var span = sequence.GetSpan(2);
        Assert.Equal(4, span.Length);
        sequence.Advance(2);

        span = sequence.GetSpan(0);
        Assert.Equal(2, span.Length);
        sequence.Advance(2);

        span = sequence.GetSpan(2);
        Assert.Equal(4, span.Length);
        sequence.Advance(4);

        span = sequence.GetSpan(2);
        Assert.Equal(4, span.Length);
        sequence.Advance(4);

        // At this point, the sequence length is 12, so the minimum new span should be 6 (half the existing length).
        Assert.Equal(6, sequence.MinimumSpanLength);
        span = sequence.GetSpan(2);
        Assert.Equal(6, span.Length);

        // Confirm that the minimum span creeps up as required to stay at 50% of the current length,
        // but never exceeds 32KB on its own.
        while (sequence.Length < 32 * 1024 * 3)
        {
            span = sequence.GetSpan(sequence.MinimumSpanLength - 1);
            Assert.Equal(sequence.MinimumSpanLength, span.Length);
            sequence.Advance(span.Length);
            Assert.Equal(Math.Min(32 * 1024, sequence.Length / 2), sequence.MinimumSpanLength);
        }

        // Now artificially raise it beyond 32KB and confirm it just stays put.
        sequence.MinimumSpanLength = 48 * 1024;
        span = sequence.GetSpan(100 * 1024);
        Assert.Equal(100 * 1024, span.Length);
        sequence.Advance(span.Length);
        Assert.Equal(48 * 1024, sequence.MinimumSpanLength);
        span = sequence.GetSpan(4);
        Assert.Equal(48 * 1024, span.Length);
    }

    [Fact]
    public void AutoIncreaseMinimumSpanLength_DoesNotResetWhenClearingSequence()
    {
        var sequence = new Sequence<int>(new MockArrayPool<int>()) { AutoIncreaseMinimumSpanLength = true, MinimumSpanLength = 4 };
        sequence.GetSpan(16);
        sequence.Advance(16);
        Assert.Equal(8, sequence.MinimumSpanLength);
        sequence.Reset();
        Assert.Equal(8, sequence.MinimumSpanLength);
    }

    [Fact]
    public void AutoIncreaseMinimumSpanLength_DoesNotReduceSpanFromMinSizePools()
    {
        var sequence = new Sequence<int>(new MockMemoryPool<int> { DefaultLength = 32 }) { AutoIncreaseMinimumSpanLength = true };
        var span = sequence.GetSpan(0);
        Assert.Equal(32, span.Length);
        sequence.Advance(32);

        // At this point, the auto-incrementing value might be 16 (half the length),
        // but if we explicitly ask for that, it may shrink the 'default' min size from the pool. That's undesirable.
        // So assert that we still get the pool size.
        span = sequence.GetSpan(0);
        Assert.Equal(32, span.Length);
        sequence.Advance(1);
    }

    [Fact]
    public void AutoIncreaseMinimumSpanLength_FalseBehavior()
    {
        var sequence = new Sequence<int>(new MockArrayPool<int>()) { AutoIncreaseMinimumSpanLength = false, MinimumSpanLength = 4 };
        var span = sequence.GetSpan(2);
        Assert.Equal(4, span.Length);
        sequence.Advance(2);

        span = sequence.GetSpan(0);
        Assert.Equal(2, span.Length);
        sequence.Advance(2);

        span = sequence.GetSpan(2);
        Assert.Equal(4, span.Length);
        sequence.Advance(4);

        span = sequence.GetSpan(2);
        Assert.Equal(4, span.Length);
        sequence.Advance(4);

        Assert.Equal(4, sequence.MinimumSpanLength);
        span = sequence.GetSpan(2);
        Assert.Equal(4, span.Length);
    }

    [Fact]
    public void SequenceOfManagedType()
    {
        var seq = new Sequence<object>();
        seq.Write(new object[] { new object(), new object() });
        Assert.Equal(2, seq.Length);
        Assert.IsType<object>(seq.AsReadOnlySequence.First.Span[0]);
        Assert.IsType<object>(seq.AsReadOnlySequence.First.Span[1]);
    }

    /// <summary>
    /// Adds a reference to an object in the sequence and returns a weak reference to it.
    /// </summary>
    /// <remarks>
    /// Don't inline this because we need to guarantee the local disappears.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StoreReferenceInSequence<T>(Sequence<T> seq)
        where T : class, new()
    {
        var o = new T();
        var tracker = new WeakReference(o);
        var span = seq.GetSpan(5);
        span[0] = o;
        seq.Advance(1);
        return tracker;
    }

    /// <summary>
    /// Adds a reference to an object in the sequence and returns a weak reference to it.
    /// </summary>
    /// <remarks>
    /// Don't inline this because we need to guarantee the local disappears.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StoreReferenceInSequence<T>(Sequence<ValueTuple<T>> seq)
        where T : class, new()
    {
        var o = new T();
        var tracker = new WeakReference(o);
        var span = seq.GetSpan(5);
        span[0] = new ValueTuple<T>(o);
        seq.Advance(1);
        return tracker;
    }
}
