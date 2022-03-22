// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams;

using System.Buffers;
using Microsoft;

/// <summary>
/// Extension methods for the <see cref="IBufferWriter{T}"/> interface.
/// </summary>
public static class BufferWriterExtensions
{
    /// <summary>
    /// Copies the content of a <see cref="ReadOnlySequence{T}"/> into an <see cref="IBufferWriter{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of element to copy.</typeparam>
    /// <param name="writer">The <see cref="IBufferWriter{T}"/> to write to.</param>
    /// <param name="sequence">The sequence to read from.</param>
    public static void Write<T>(this IBufferWriter<T> writer, ReadOnlySequence<T> sequence)
    {
        Requires.NotNull(writer, nameof(writer));

        foreach (ReadOnlyMemory<T> memory in sequence)
        {
            writer.Write(memory.Span);
        }
    }
}
