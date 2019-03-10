// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;

    /// <summary>
    /// An <see cref="IBufferWriter{T}"/> that reserves some fixed size for a header.
    /// </summary>
    /// <typeparam name="T">The type of element written by this writer.</typeparam>
    /// <remarks>
    /// This type is used for inserting the length of list in the header when the length is not known beforehand.
    /// It is optimized to minimize or avoid copying.
    /// </remarks>
    public class PrefixingBufferWriter<T> : IBufferWriter<T>
    {
        /// <summary>
        /// The value to use in place of <see cref="payloadSizeHint"/> when it is 0.
        /// </summary>
        /// <remarks>
        /// We choose ~4K, since 4K is the default size for buffers in a lot of corefx libraries.
        /// We choose 4K - 4 specifically because length prefixing is so often for an <see cref="int"/> value,
        /// and if we ask for 1 byte more than 4K, memory pools tend to give us 8K.
        /// </remarks>
        private const int PayloadSizeGuess = 4092;

        /// <summary>
        /// The underlying buffer writer.
        /// </summary>
        private readonly IBufferWriter<T> innerWriter;

        /// <summary>
        /// The length of the prefix to reserve space for.
        /// </summary>
        private readonly int expectedPrefixSize;

        /// <summary>
        /// The minimum space to reserve for the payload when first asked for a buffer.
        /// </summary>
        /// <remarks>
        /// This, added to <see cref="expectedPrefixSize"/>, makes up the minimum size to request from <see cref="innerWriter"/>
        /// to minimize the chance that we'll need to copy buffers from <see cref="excessSequence"/> to <see cref="innerWriter"/>.
        /// </remarks>
        private readonly int payloadSizeHint;

        /// <summary>
        /// The pool to use when initializing <see cref="excessSequence"/>.
        /// </summary>
        private readonly MemoryPool<T> memoryPool;

        /// <summary>
        /// The buffer writer to use for all buffers after the original one obtained from <see cref="innerWriter"/>.
        /// </summary>
        private Sequence<T> excessSequence;

        /// <summary>
        /// The buffer from <see cref="innerWriter"/> reserved for the fixed-length prefix.
        /// </summary>
        private Memory<T> prefixMemory;

        /// <summary>
        /// The memory being actively written to, which may have come from <see cref="innerWriter"/> or <see cref="excessSequence"/>.
        /// </summary>
        private Memory<T> realMemory;

        /// <summary>
        /// The number of elements written to the original buffer obtained from <see cref="innerWriter"/>.
        /// </summary>
        private int advanced;

        /// <summary>
        /// A value indicating whether we're using <see cref="excessSequence"/> in the current state.
        /// </summary>
        private bool usingExcessMemory;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefixingBufferWriter{T}"/> class.
        /// </summary>
        /// <param name="innerWriter">The underlying writer that should ultimately receive the prefix and payload.</param>
        /// <param name="prefixSize">The length of the header to reserve space for. Must be a positive number.</param>
        /// <param name="payloadSizeHint">A hint at the expected max size of the payload. The real size may be more or less than this, but additional copying is avoided if it does not exceed this amount. If 0, a reasonable guess is made.</param>
        /// <param name="memoryPool">The memory pool to use for allocating additional memory when the payload exceeds <paramref name="payloadSizeHint"/>.</param>
        public PrefixingBufferWriter(IBufferWriter<T> innerWriter, int prefixSize, int payloadSizeHint = 0, MemoryPool<T> memoryPool = null)
        {
            if (prefixSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(prefixSize));
            }

            this.innerWriter = innerWriter ?? throw new ArgumentNullException(nameof(innerWriter));
            this.expectedPrefixSize = prefixSize;
            this.payloadSizeHint = payloadSizeHint;
            this.memoryPool = memoryPool ?? MemoryPool<T>.Shared;
        }

        /// <summary>
        /// Gets the sum of all values passed to <see cref="Advance(int)"/> since
        /// the last call to <see cref="Commit"/>.
        /// </summary>
        public long Length => (this.excessSequence?.Length ?? 0) + this.advanced;

        /// <summary>
        /// Gets the memory reserved for the prefix.
        /// </summary>
        public Memory<T> Prefix
        {
            get
            {
                this.EnsureInitialized(0);
                return this.prefixMemory;
            }
        }

        /// <inheritdoc />
        public void Advance(int count)
        {
            if (this.usingExcessMemory)
            {
                this.excessSequence.Advance(count);
                this.realMemory = default;
            }
            else
            {
                this.realMemory = this.realMemory.Slice(count);
                this.advanced += count;
            }
        }

        /// <inheritdoc />
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            this.EnsureInitialized(sizeHint);
            return this.realMemory;
        }

        /// <inheritdoc />
        public Span<T> GetSpan(int sizeHint = 0)
        {
            this.EnsureInitialized(sizeHint);
            return this.realMemory.Span;
        }

        /// <summary>
        /// Commits all the elements written and the prefix to the underlying writer
        /// and advances the underlying writer past the prefix and payload.
        /// </summary>
        /// <remarks>
        /// This instance is safe to reuse after this call.
        /// </remarks>
        public void Commit()
        {
            if (this.prefixMemory.Length == 0)
            {
                // No payload was actually written, and we never requested memory, so just write it out.
                this.innerWriter.Write(this.Prefix.Span);
            }
            else
            {
                // Payload has been written. Write in the prefix and commit the first buffer.
                this.innerWriter.Advance(this.prefixMemory.Length + this.advanced);

                // Now copy any excess buffer.
                if (this.usingExcessMemory)
                {
                    var span = this.innerWriter.GetSpan((int)this.excessSequence.Length);
                    foreach (var segment in this.excessSequence.AsReadOnlySequence)
                    {
                        segment.Span.CopyTo(span);
                        span = span.Slice(segment.Length);
                    }

                    this.innerWriter.Advance((int)this.excessSequence.Length);
                    this.excessSequence.Reset(); // return backing arrays to memory pools
                }
            }

            // Reset for the next write.
            this.usingExcessMemory = false;
            this.prefixMemory = default;
            this.realMemory = default;
            this.advanced = 0;
        }

        private void EnsureInitialized(int sizeHint)
        {
            if (this.prefixMemory.Length == 0)
            {
                int sizeToRequest = this.expectedPrefixSize + Math.Max(sizeHint, this.payloadSizeHint == 0 ? PayloadSizeGuess : this.payloadSizeHint);
                var memory = this.innerWriter.GetMemory(sizeToRequest);
                this.prefixMemory = memory.Slice(0, this.expectedPrefixSize);
                this.realMemory = memory.Slice(this.expectedPrefixSize);
            }
            else if (this.realMemory.Length == 0 || this.realMemory.Length - this.advanced < sizeHint)
            {
                if (this.excessSequence == null)
                {
                    this.excessSequence = new Sequence<T>(this.memoryPool);
                }

                this.usingExcessMemory = true;
                this.realMemory = this.excessSequence.GetMemory(sizeHint);
            }
        }
    }
}
