// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams;

using System.Buffers;
using Microsoft;

/// <summary>
/// Extension methods for the <see cref="ReadOnlySequence{T}"/> type.
/// </summary>
public static class ReadOnlySequenceExtensions
{
    /// <summary>
    /// Copies the content of one <see cref="ReadOnlySequence{T}"/> to another that is backed by its own
    /// memory buffers.
    /// </summary>
    /// <typeparam name="T">The type of element in the sequence.</typeparam>
    /// <param name="template">The sequence to copy from.</param>
    /// <returns>A shallow copy of the sequence, backed by buffers which will never be recycled.</returns>
    /// <remarks>
    /// This method is useful for retaining data that is backed by buffers that will be reused later.
    /// </remarks>
    public static ReadOnlySequence<T> Clone<T>(this ReadOnlySequence<T> template)
    {
        Sequence<T> sequence = new();
        sequence.Write(template);
        return sequence;
    }

    /// <summary>
    /// Compares the contents of two <see cref="ReadOnlySequence{T}"/> instances for equality.
    /// </summary>
    /// <typeparam name="T">The type of element stored in the sequences.</typeparam>
    /// <param name="left">The first sequence.</param>
    /// <param name="right">The second sequence.</param>
    /// <returns><see langword="true" /> if the sequences have equal content; <see langword="false" /> otherwise.</returns>
    /// <remarks>
    /// The underlying buffers need not be reference equal, nor must the segments in the sequences be of the same size.
    /// </remarks>
    public static bool SequenceEqual<T>(this in ReadOnlySequence<T> left, in ReadOnlySequence<T> right)
#if !NET8_0_OR_GREATER
        where T : IEquatable<T>
#endif
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        if (left.IsSingleSegment && right.IsSingleSegment)
        {
#if NETSTANDARD2_1 || NET
            return left.FirstSpan.SequenceEqual(right.FirstSpan);
#else
            return left.First.Span.SequenceEqual(right.First.Span);
#endif
        }

        ReadOnlySequence<T>.Enumerator aEnumerator = left.GetEnumerator();
        ReadOnlySequence<T>.Enumerator bEnumerator = right.GetEnumerator();

        ReadOnlySpan<T> aCurrent = default;
        ReadOnlySpan<T> bCurrent = default;
        while (true)
        {
            bool aNext = TryGetNonEmptySpan(ref aEnumerator, ref aCurrent);
            bool bNext = TryGetNonEmptySpan(ref bEnumerator, ref bCurrent);
            if (!aNext && !bNext)
            {
                // We've reached the end of both sequences at the same time.
                return true;
            }
            else if (aNext != bNext)
            {
                // One ran out of bytes before the other.
                // We don't anticipate this, because we already checked the lengths.
                throw Assumes.NotReachable();
            }

            int commonLength = Math.Min(aCurrent.Length, bCurrent.Length);
            if (!aCurrent[..commonLength].SequenceEqual(bCurrent[..commonLength]))
            {
                return false;
            }

            aCurrent = aCurrent.Slice(commonLength);
            bCurrent = bCurrent.Slice(commonLength);
        }

        static bool TryGetNonEmptySpan(ref ReadOnlySequence<T>.Enumerator enumerator, ref ReadOnlySpan<T> span)
        {
            while (span.Length == 0)
            {
                if (!enumerator.MoveNext())
                {
                    return false;
                }

                span = enumerator.Current.Span;
            }

            return true;
        }
    }

    /// <summary>
    /// Polyfill method used by the <see cref="SequenceReader{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of element kept by the sequence.</typeparam>
    /// <param name="sequence">The sequence to retrieve.</param>
    /// <param name="first">The first span in the sequence.</param>
    /// <param name="next">The next position.</param>
    internal static void GetFirstSpan<T>(this ReadOnlySequence<T> sequence, out ReadOnlySpan<T> first, out SequencePosition next)
    {
        first = sequence.First.Span;
        next = sequence.GetPosition(first.Length);
    }
}
