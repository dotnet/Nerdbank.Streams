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
    public class Sequence<T>
    {
        private readonly Stack<SequenceSegment> segmentPool = new Stack<SequenceSegment>();

        private readonly MemoryPool<T> memoryPool;

        private SequenceSegment first;

        private SequenceSegment last;

        public Sequence()
            : this(MemoryPool<T>.Shared)
        {
        }

        public Sequence(MemoryPool<T> memoryPool)
        {
            Requires.NotNull(memoryPool, nameof(memoryPool));
            this.memoryPool = memoryPool;
        }

        public static implicit operator ReadOnlySequence<T>(Sequence<T> sequence)
        {
            return sequence.first != null
                ? new ReadOnlySequence<T>(sequence.first, 0, sequence.last, sequence.last.Length)
                : ReadOnlySequence<T>.Empty;
        }

        public void Append(IMemoryOwner<T> array) => this.Append(array, 0, Requires.NotNull(array, nameof(array)).Memory.Length);

        public void Append(IMemoryOwner<T> array, int length) => this.Append(array, 0, length);

        public void Append(IMemoryOwner<T> array, int start, int length)
        {
            Requires.NotNull(array, nameof(array));
            Requires.Range(length >= 0 && start + length <= array.Memory.Length, nameof(length));

            var segment = this.segmentPool.Count > 0 ? this.segmentPool.Pop() : new SequenceSegment();
            segment.SetMemory(array, start, start + length);
            if (this.last == null)
            {
                this.first = this.last = segment;
            }
            else
            {
                this.last.SetNext(segment);
                this.last = segment;
            }
        }

        public void Advance(SequencePosition position)
        {
            // TODO: protect against SequencePosition arguments that do not represent a FORWARD position for THIS sequence.

            var firstSegment = (SequenceSegment)position.GetObject();
            int firstIndex = position.GetInteger();

            var oldFirst = this.first;
            while (oldFirst != firstSegment)
            {
                oldFirst.ResetMemory();
                oldFirst = oldFirst.Next;
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

        // TODO: change return type to Memory<T>
        public IMemoryOwner<T> GetMemory(int minimumLength)
        {
            Requires.Range(minimumLength > 0, nameof(minimumLength));

            // TODO: return slack space in this.last if there is adequate.
            //       Require a pattern of GetMemory/Advance like PipeWriter does.

            return this.memoryPool.Rent(minimumLength);
        }

        public ReadOnlySequence<T> AsReadOnlySequence() => this;

        public void Reset()
        {
            var current = this.first;
            while (current != null)
            {
                current = this.RecycleAndGetNext(current);
            }

            this.first = this.last = null;
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
                    Debug.Assert(value - this.Start <= this.AvailableMemory.Length, "value - this.Start <= this.AvailableMemory.Length");

                    this.end = value;
                    this.UpdateMemory();
                }
            }

            internal IMemoryOwner<T> MemoryOwner { get; private set; }

            internal Memory<T> AvailableMemory { get; private set; }

            internal int Length => this.End - this.Start;

            /// <summary>
            /// Gets a value indicating whether data should not be written into the backing block after the End offset.
            /// Data between start and end should never be modified since this would break cloning.
            /// </summary>
            internal bool ReadOnly { get; private set; }

            /// <summary>
            /// Gets the amount of writable bytes in this segment. It is the amount of bytes between <see cref="Length"/> and <see cref="End"/>.
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
                Debug.Assert(segment != null, "segment != null");
                Debug.Assert(this.Next == null, "this.Next == null");

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
                Debug.Assert(this.Start + offset <= this.End, "this.Start + offset <= this.End");
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
