// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// Internal utilities.
    /// </summary>
    internal static class Utilities
    {
        /// <summary>
        /// A completed task.
        /// </summary>
        internal static readonly Task CompletedTask = Task.FromResult(0);

        /// <summary>
        /// Validates that a buffer is not null and that its index and count refer to valid positions within the buffer.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <param name="buffer">The array to check.</param>
        /// <param name="index">The starting position within the buffer.</param>
        /// <param name="count">The number of elements to process in the buffer.</param>
        internal static void ValidateBufferIndexAndCount<T>(T[] buffer, int index, int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(index >= 0, nameof(index));
            Requires.Range(count >= 0, nameof(count));
            if (index + count > buffer.Length)
            {
                throw new ArgumentException();
            }
        }

        /// <summary>
        /// Removes an element from the middle of a queue without disrupting the other elements.
        /// </summary>
        /// <typeparam name="T">The element to remove.</typeparam>
        /// <param name="queue">The queue to modify.</param>
        /// <param name="valueToRemove">The value to remove.</param>
        /// <returns><c>true</c> if the value was found and removed; <c>false</c> if no match was found.</returns>
        /// <remarks>
        /// If a value appears multiple times in the queue, only its first entry is removed.
        /// </remarks>
        internal static bool RemoveMidQueue<T>(this Queue<T> queue, T valueToRemove)
            where T : class
        {
            Requires.NotNull(queue, nameof(queue));
            Requires.NotNull(valueToRemove, nameof(valueToRemove));

            int originalCount = queue.Count;
            int dequeueCounter = 0;
            bool found = false;
            while (dequeueCounter < originalCount)
            {
                dequeueCounter++;
                T dequeued = queue.Dequeue();
                if (!found && dequeued == valueToRemove)
                { // only find 1 match
                    found = true;
                }
                else
                {
                    queue.Enqueue(dequeued);
                }
            }

            return found;
        }

        internal static Task WaitForReaderCompletionAsync(this PipeWriter writer)
        {
            Requires.NotNull(writer, nameof(writer));

            var readerDone = new TaskCompletionSource<object>();
            writer.OnReaderCompleted(
                (ex, tcsObject) =>
                {
                    var tcs = (TaskCompletionSource<object>)tcsObject;
                    if (ex != null)
                    {
                        tcs.SetException(ex);
                    }
                    else
                    {
                        tcs.SetResult(null);
                    }
                },
                readerDone);
            return readerDone.Task;
        }

        internal static Task WaitForWriterCompletionAsync(this PipeReader reader)
        {
            Requires.NotNull(reader, nameof(reader));

            var writerDone = new TaskCompletionSource<object>();
            reader.OnWriterCompleted(
                (ex, wdObject) =>
                {
                    var wd = (TaskCompletionSource<object>)wdObject;
                    if (ex != null)
                    {
                        wd.SetException(ex);
                    }
                    else
                    {
                        wd.SetResult(null);
                    }
                },
                writerDone);
            return writerDone.Task;
        }

        internal static Task FlushIfNecessaryAsync(this Stream stream, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));

#if !NETSTANDARD1_6
            // PipeStream.Flush does nothing, and its FlushAsync method isn't overridden
            // so calling FlushAsync simply allocates memory to schedule a no-op sync method.
            // So skip the call in that case.
            if (stream is System.IO.Pipes.PipeStream)
            {
                return Task.CompletedTask;
            }
#endif

            return stream.FlushAsync();
        }

        /// <summary>
        /// Reads an <see cref="int"/> value from a buffer using big endian.
        /// </summary>
        /// <param name="buffer">The buffer to read from. Must be at most 4 bytes long.</param>
        /// <returns>The read value.</returns>
        internal static int ReadInt(ReadOnlySpan<byte> buffer)
        {
            Requires.Argument(buffer.Length <= 4, nameof(buffer), "Int32 length exceeded.");

            int local = 0;
            for (int offset = 0; offset < buffer.Length; offset++)
            {
                local <<= 8;
                local |= buffer[offset];
            }

            return local;
        }

        /// <summary>
        /// Writes an <see cref="int"/> value to a buffer using big endian.
        /// </summary>
        /// <param name="buffer">The buffer to write to. Must be at least 4 bytes long.</param>
        /// <param name="value">The value to write.</param>
        internal static void Write(Span<byte> buffer, int value)
        {
            buffer[0] = (byte)(value >> 24);
            buffer[1] = (byte)(value >> 16);
            buffer[2] = (byte)(value >> 8);
            buffer[3] = (byte)value;
        }

        /// <summary>
        /// Writes an <see cref="ushort"/> value to a buffer using big endian.
        /// </summary>
        /// <param name="buffer">The buffer to write to. Must be at least 2 bytes long.</param>
        /// <param name="value">The value to write.</param>
        internal static void Write(Span<byte> buffer, ushort value)
        {
            buffer[0] = (byte)(value >> 8);
            buffer[1] = (byte)value;
        }
    }
}
