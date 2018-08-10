// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using Microsoft;

    /// <summary>
    /// Manages a sequence of elements, readily castable as a <see cref="ReadOnlySequence{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of element stored by the sequence.</typeparam>
    /// <remarks>
    /// Instance members are not thread-safe.
    /// </remarks>
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
    public class Sequence<T> : IBufferWriter<T>
    {
        private const int DefaultBufferSize = 4 * 1024;

        private readonly Stack<SequenceSegment> segmentPool = new Stack<SequenceSegment>();

        private readonly MemoryPool<T> memoryPool;

        private SequenceSegment first;

        private SequenceSegment last;

        /// <summary>
        /// Initializes a new instance of the <see cref="Sequence{T}"/> class.
        /// </summary>
        public Sequence()
            : this(MemoryPool<T>.Shared)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Sequence{T}"/> class.
        /// </summary>
        /// <param name="memoryPool">The pool to use for recycling backing arrays.</param>
        public Sequence(MemoryPool<T> memoryPool)
        {
            Requires.NotNull(memoryPool, nameof(memoryPool));
            this.memoryPool = memoryPool;
        }

        /// <summary>
        /// Gets this sequence expressed as a <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        /// <returns>A read only sequence representing the data in this object.</returns>
        public ReadOnlySequence<T> AsReadOnlySequence => this;

        /// <summary>
        /// Gets the value to display in a debugger datatip.
        /// </summary>
        private string DebuggerDisplay => $"Length: {AsReadOnlySequence.Length}";

        /// <summary>
        /// Expresses this sequence as a <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        /// <param name="sequence">The sequence to convert.</param>
        public static implicit operator ReadOnlySequence<T>(Sequence<T> sequence)
        {
            return sequence.first != null
                ? new ReadOnlySequence<T>(sequence.first, 0, sequence.last, sequence.last.Length)
                : ReadOnlySequence<T>.Empty;
        }

        /// <summary>
        /// Removes all elements from the sequence from its beginning to the specified position,
        /// considering that data to have been fully processed.
        /// </summary>
        /// <param name="position">
        /// The position of the first element that has not yet been processed.
        /// This is typically <see cref="ReadOnlySequence{T}.End"/> after reading all elements from that instance.
        /// </param>
        public void AdvanceTo(SequencePosition position)
        {
            var firstSegment = (SequenceSegment)position.GetObject();
            int firstIndex = position.GetInteger();

            // Before making any mutations, confirm that the block specified belongs to this sequence.
            var current = this.first;
            while (current != firstSegment && current != null)
            {
                current = current.Next;
            }

            Requires.Argument(current != null, nameof(position), "Position does not represent a valid position in this sequence.");

            // Also confirm that the position is not a prior position in the block.
            Requires.Argument(firstIndex >= current.Start, nameof(position), "Position must not be earlier than current position.");

            // Now repeat the loop, performing the mutations.
            current = this.first;
            while (current != firstSegment)
            {
                current.ResetMemory();
                current = current.Next;
            }

            firstSegment.AdvanceTo(firstIndex);

            if (firstSegment.Length == 0)
            {
                firstSegment = this.RecycleAndGetNext(firstSegment);
            }

            this.first = firstSegment;

            if (this.first == null)
            {
                this.last = null;
            }
        }

        /// <summary>
        /// Advances the sequence to include the specified number of elements initialized into memory
        /// returned by a prior call to <see cref="GetMemory(int)"/>.
        /// </summary>
        /// <param name="count">The number of elements written into memory.</param>
        public void Advance(int count)
        {
            Requires.Range(count >= 0, nameof(count));
            this.last.End += count;
        }

        /// <summary>
        /// Gets writable memory that can be initialized and added to the sequence via a subsequent call to <see cref="Advance(int)"/>.
        /// </summary>
        /// <param name="sizeHint">The size of the memory required, or 0 to just get a convenient (non-empty) buffer.</param>
        /// <returns>The requested memory.</returns>
        public Memory<T> GetMemory(int sizeHint)
        {
            Requires.Range(sizeHint >= 0, nameof(sizeHint));

            if (sizeHint == 0)
            {
                if (this.last?.WritableBytes > 0)
                {
                    sizeHint = this.last.WritableBytes;
                }
                else
                {
                    sizeHint = DefaultBufferSize;
                }
            }

            if (this.last == null || this.last.WritableBytes < sizeHint)
            {
                this.Append(this.memoryPool.Rent(sizeHint));
            }

            return this.last.TrailingSlack;
        }

        /// <summary>
        /// Gets writable memory that can be initialized and added to the sequence via a subsequent call to <see cref="Advance(int)"/>.
        /// </summary>
        /// <param name="sizeHint">The size of the memory required, or 0 to just get a convenient (non-empty) buffer.</param>
        /// <returns>The requested memory.</returns>
        public Span<T> GetSpan(int sizeHint) => this.GetMemory(sizeHint).Span;

        /// <summary>
        /// Clears the entire sequence and releases associated memory.
        /// </summary>
        public void Reset()
        {
            var current = this.first;
            while (current != null)
            {
                current = this.RecycleAndGetNext(current);
            }

            this.first = this.last = null;
        }

        private void Append(IMemoryOwner<T> array)
        {
            Requires.NotNull(array, nameof(array));

            var segment = this.segmentPool.Count > 0 ? this.segmentPool.Pop() : new SequenceSegment();
            segment.SetMemory(array, 0, 0);

            if (this.last == null)
            {
                this.first = this.last = segment;
            }
            else
            {
                if (this.last.Length > 0)
                {
                    // Add a new block.
                    this.last.SetNext(segment);
                }
                else
                {
                    // The last block is completely unused. Replace it instead of appending to it.
                    var current = this.first;
                    if (this.first != this.last)
                    {
                        while (current.Next != this.last)
                        {
                            current = current.Next;
                        }
                    }
                    else
                    {
                        this.first = segment;
                    }

                    current.SetNext(segment);
                    this.RecycleAndGetNext(this.last);
                }

                this.last = segment;
            }
        }

        private SequenceSegment RecycleAndGetNext(SequenceSegment segment)
        {
            segment.ResetMemory();
            var recycledSegment = segment;
            segment = segment.Next;
            this.segmentPool.Push(recycledSegment);
            return segment;
        }

        private class SequenceSegment : ReadOnlySequenceSegment<T>
        {
            /// <summary>
            /// Backing field for the <see cref="End"/> property.
            /// </summary>
            private int end;

            /// <summary>
            /// Gets the index of the first element in the backing array to consider part of the sequence.
            /// </summary>
            /// <remarks>
            /// The Start represents the offset into AvailableMemory where the range of "active" bytes begins. At the point when the block is leased
            /// the Start is guaranteed to be equal to 0. The value of Start may be assigned anywhere between 0 and
            /// AvailableMemory.Length, and must be equal to or less than End.
            /// </remarks>
            internal int Start { get; private set; }

            /// <summary>
            /// Gets or sets the index of the element just beyond the end in the backing array to consider part of the sequence.
            /// </summary>
            /// <remarks>
            /// The End represents the offset into AvailableMemory where the range of "active" bytes ends. At the point when the block is leased
            /// the End is guaranteed to be equal to Start. The value of Start may be assigned anywhere between 0 and
            /// Buffer.Length, and must be equal to or less than End.
            /// </remarks>
            internal int End
            {
                get => this.end;
                set
                {
                    Requires.Range(value - this.Start <= this.AvailableMemory.Length, nameof(value));

                    this.end = value;
                    this.UpdateMemory();
                }
            }

            internal Memory<T> TrailingSlack => this.AvailableMemory.Slice(this.End);

            internal IMemoryOwner<T> MemoryOwner { get; private set; }

            internal Memory<T> AvailableMemory { get; private set; }

            internal int Length => this.End - this.Start;

            /// <summary>
            /// Gets a value indicating whether data should not be written into the backing block after the End offset.
            /// Data between start and end should never be modified since this would break cloning.
            /// </summary>
            internal bool ReadOnly { get; private set; }

            /// <summary>
            /// Gets the amount of writable bytes in this segment.
            /// It is the amount of bytes between <see cref="Length"/> and <see cref="End"/>.
            /// </summary>
            internal int WritableBytes
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => this.AvailableMemory.Length - this.End;
            }

            internal new SequenceSegment Next
            {
                get => (SequenceSegment)base.Next;
                set => base.Next = value;
            }

            internal void SetMemory(IMemoryOwner<T> memoryOwner)
            {
                this.SetMemory(memoryOwner, 0, memoryOwner.Memory.Length);
            }

            internal void SetMemory(IMemoryOwner<T> memoryOwner, int start, int end, bool readOnly = false)
            {
                this.MemoryOwner = memoryOwner;

                this.AvailableMemory = this.MemoryOwner.Memory;

                this.ReadOnly = readOnly;
                this.RunningIndex = 0;
                this.Start = start;
                this.End = end;
                this.Next = null;
            }

            internal void ResetMemory()
            {
                this.MemoryOwner.Dispose();
                this.MemoryOwner = null;
                this.AvailableMemory = default;
            }

            internal void SetNext(SequenceSegment segment)
            {
                Requires.NotNull(segment, nameof(segment));

                this.Next = segment;

                segment = this;

                while (segment.Next != null)
                {
                    segment.Next.RunningIndex = segment.RunningIndex + segment.Length;
                    segment = segment.Next;
                }
            }

            internal void AdvanceTo(int offset)
            {
                Requires.Range(this.Start + offset <= this.End, nameof(offset));
                this.Start += offset;
                this.UpdateMemory();

                var segment = this;

                while (segment.Next != null)
                {
                    segment.Next.RunningIndex = segment.RunningIndex + segment.Length;
                    segment = segment.Next;
                }
            }

            private void UpdateMemory()
            {
                this.Memory = this.AvailableMemory.Slice(this.Start, this.end - this.Start);
            }
        }
    }
}
