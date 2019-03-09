// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.Linq;
using Nerdbank.Streams;
using Xunit;

public class PrefixingBufferWriterTests
{
    private const int PrefixSize = 4;
    private const int PayloadSize = 10;

    private static readonly ReadOnlyMemory<byte> Prefix = new byte[PrefixSize] { 0xcc, 0xdd, 0xee, 0xaa };

    private static readonly ReadOnlyMemory<byte> Payload = Enumerable.Range(3, PayloadSize).Select(v => (byte)v).ToArray();

    private readonly MockPool<byte> mockPool = new MockPool<byte>();

    private readonly Sequence<byte> sequence;

    public PrefixingBufferWriterTests()
    {
        this.sequence = new Sequence<byte>(this.mockPool);
    }

    [Fact]
    public void NoPayload()
    {
        var prefixWriter = new PrefixingBufferWriter<byte>(this.sequence, Prefix.Length, 50);
        Assert.Equal(0, this.sequence.Length);
        prefixWriter.Complete(Prefix.Span);
        Assert.Equal(Prefix.Length, this.sequence.Length);
        Assert.Equal(Prefix.ToArray(), this.sequence.AsReadOnlySequence.ToArray());
    }

    [Theory]
    [PairwiseData]
    public void SomePayload(
        bool largArrayPool,
        bool excessSpan,
        [CombinatorialValues(0, PayloadSize - 1, PayloadSize, PayloadSize + 1)] int sizeHint,
        [CombinatorialValues(1, 2, 3, PayloadSize)] int stepCount)
    {
        this.mockPool.MinArraySizeFactor = largArrayPool ? 2.0 : 1.0;

        var prefixWriter = new PrefixingBufferWriter<byte>(this.sequence, Prefix.Length, sizeHint);
        int stepSize = Payload.Length / stepCount;
        int expectedLength = 0;
        for (int i = 0; i < stepCount - 1; i++)
        {
            var spanToWrite = Payload.Span.Slice(stepSize * i, stepSize);
            if (excessSpan)
            {
                var targetSpan = prefixWriter.GetSpan((int)(spanToWrite.Length * 1.5));
                spanToWrite.CopyTo(targetSpan);
                prefixWriter.Advance(spanToWrite.Length);
            }
            else
            {
                prefixWriter.Write(spanToWrite);
            }

            expectedLength += spanToWrite.Length;
            Assert.Equal(expectedLength, prefixWriter.Length);
        }

        // The last step fills in the remainder as well.
        prefixWriter.Write(Payload.Span.Slice(stepSize * (stepCount - 1)));

        this.PayloadCompleteHelper(prefixWriter);
    }

    [Fact]
    public void GetSpan_WriteWithHint0()
    {
        var prefixWriter = new PrefixingBufferWriter<byte>(this.sequence, Prefix.Length, 0);
        var span = prefixWriter.GetSpan(0);
        Assert.NotEqual(0, span.Length);
    }

    [Fact]
    public void GetSpan_WriteFillThenRequest0()
    {
        var prefixWriter = new PrefixingBufferWriter<byte>(this.sequence, Prefix.Length, 0);
        var span = prefixWriter.GetSpan(5);
        prefixWriter.Advance(span.Length);
        span = prefixWriter.GetSpan(0);
        Assert.NotEqual(0, span.Length);
    }

    [Fact]
    public void GetMemory()
    {
        var prefixWriter = new PrefixingBufferWriter<byte>(this.sequence, Prefix.Length, 0);
        var mem = prefixWriter.GetMemory(Payload.Length);
        Assert.NotEqual(0, mem.Length);
        Payload.CopyTo(mem);
        prefixWriter.Advance(Payload.Length);
        this.PayloadCompleteHelper(prefixWriter);
    }

    [Fact]
    public void ReuseAfterComplete()
    {
        var prefixWriter = new PrefixingBufferWriter<byte>(this.sequence, Prefix.Length, 0);
        prefixWriter.Write(Payload.Span);
        Assert.Equal(Payload.Length, prefixWriter.Length);
        this.PayloadCompleteHelper(prefixWriter);
        this.sequence.Reset();

        Assert.Equal(0, prefixWriter.Length);
        prefixWriter.Write(Payload.Span);
        Assert.Equal(Payload.Length, prefixWriter.Length);
        this.PayloadCompleteHelper(prefixWriter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_NonPositivePrefixHintSizes(int size)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new PrefixingBufferWriter<byte>(this.sequence, size));
        Assert.Equal("prefixSize", ex.ParamName);
    }

    [Fact]
    public void Ctor_NullUnderwriter()
    {
        Assert.Throws<ArgumentNullException>(() => new PrefixingBufferWriter<byte>(null, 5));
    }

    [Fact]
    public void Complete_PrefixLengthMismatch()
    {
        var prefixing = new PrefixingBufferWriter<byte>(this.sequence, 5);
        var ex = Assert.Throws<ArgumentException>(() => prefixing.Complete(new byte[3]));
        Assert.Equal("prefix", ex.ParamName);
    }

    private void PayloadCompleteHelper(PrefixingBufferWriter<byte> prefixWriter)
    {
        // There mustn't be any calls to Advance on the underlying buffer yet, or else we've lost the opportunity to write the prefix.
        Assert.Equal(0, this.sequence.Length);
        var length = prefixWriter.Length;

        // Go ahead and commit everything, with our prefix.
        prefixWriter.Complete(Prefix.Span);

        Assert.Equal(length + Prefix.Length, this.sequence.Length);

        // Verify that the prefix immediately precedes the payload.
        Assert.Equal(Prefix.ToArray().Concat(Payload.ToArray()), this.sequence.AsReadOnlySequence.ToArray());
    }
}
