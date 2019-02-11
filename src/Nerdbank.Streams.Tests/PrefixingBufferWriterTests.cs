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
        }

        // The last step fills in the remainder as well.
        prefixWriter.Write(Payload.Span.Slice(stepSize * (stepCount - 1)));

        this.PayloadCompleteHelper(prefixWriter);
    }

    private void PayloadCompleteHelper(PrefixingBufferWriter<byte> prefixWriter)
    {
        // There mustn't be any calls to Advance on the underlying buffer yet, or else we've lost the opportunity to write the prefix.
        Assert.Equal(0, this.sequence.Length);

        // Go ahead and commit everything, with our prefix.
        prefixWriter.Complete(Prefix.Span);

        // Verify that the prefix immediately precedes the payload.
        Assert.Equal(Prefix.ToArray().Concat(Payload.ToArray()), this.sequence.AsReadOnlySequence.ToArray());
    }
}
