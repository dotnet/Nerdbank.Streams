// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams;

using System.Buffers;

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
