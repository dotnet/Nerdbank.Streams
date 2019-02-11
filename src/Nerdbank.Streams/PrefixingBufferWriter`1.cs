// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;

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
        /// The underlying buffer writer.
        /// </summary>
        private readonly IBufferWriter<T> innerWriter;

        /// <summary>
        /// The length of the header.
        /// </summary>
        private readonly int expectedPrefixSize;

        /// <summary>
        /// A hint from our owner at the size of the payload that follows the header.
        /// </summary>
        private readonly int payloadSizeHint;

        /// <summary>
        /// The memory reserved for the header from the <see cref="innerWriter"/>.
        /// This memory is not reserved until the first call from this writer to acquire memory.
        /// </summary>
        private Memory<T> prefixMemory;

        /// <summary>
        /// The memory acquired from <see cref="innerWriter"/>.
        /// This memory is not reserved until the first call from this writer to acquire memory.
        /// </summary>
        private Memory<T> realMemory;

        /// <summary>
        /// The number of elements written to a buffer belonging to <see cref="innerWriter"/>.
        /// </summary>
        private int advanced;

        /// <summary>
        /// The fallback writer to use when the caller writes more than we allowed for given the <see cref="payloadSizeHint"/>
        /// in anything but the initial call to <see cref="GetSpan(int)"/>.
        /// </summary>
        private Sequence<T> privateWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefixingBufferWriter{T}"/> class.
        /// </summary>
        /// <param name="innerWriter">The underlying writer that should ultimately receive the prefix and payload.</param>
        /// <param name="prefixSize">The length of the header to reserve space for. Must be a positive number.</param>
        /// <param name="payloadSizeHint">A hint at the expected max size of the payload. The real size may be more or less than this, but additional copying is avoided if it does not exceed this amount. If 0, a reasonable guess is made.</param>
        public PrefixingBufferWriter(IBufferWriter<T> innerWriter, int prefixSize, int payloadSizeHint)
        {
            if (prefixSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(prefixSize));
            }

            this.innerWriter = innerWriter ?? throw new ArgumentNullException(nameof(innerWriter));
            this.expectedPrefixSize = prefixSize;
            this.payloadSizeHint = payloadSizeHint;
        }

        /// <inheritdoc />
        public void Advance(int count)
        {
            if (this.privateWriter != null)
            {
                this.privateWriter.Advance(count);
            }
            else
            {
                this.advanced += count;
            }
        }

        /// <inheritdoc />
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            this.EnsureInitialized(sizeHint);

            if (this.privateWriter != null || sizeHint > this.realMemory.Length - this.advanced)
            {
                if (this.privateWriter == null)
                {
                    this.privateWriter = new Sequence<T>();
                }

                return this.privateWriter.GetMemory(sizeHint);
            }
            else
            {
                return this.realMemory.Slice(this.advanced);
            }
        }

        /// <inheritdoc />
        public Span<T> GetSpan(int sizeHint = 0)
        {
            this.EnsureInitialized(sizeHint);

            if (this.privateWriter != null || sizeHint > this.realMemory.Length - this.advanced)
            {
                if (this.privateWriter == null)
                {
                    this.privateWriter = new Sequence<T>();
                }

                return this.privateWriter.GetSpan(sizeHint);
            }
            else
            {
                return this.realMemory.Span.Slice(this.advanced);
            }
        }

        /// <summary>
        /// Inserts the prefix and commits the payload to the underlying <see cref="IBufferWriter{T}"/>.
        /// </summary>
        /// <param name="prefix">The prefix to write in. The length must match the one given in the constructor.</param>
        public void Complete(ReadOnlySpan<T> prefix)
        {
            if (prefix.Length != this.expectedPrefixSize)
            {
                throw new ArgumentOutOfRangeException(nameof(prefix), "Prefix was not expected length.");
            }

            if (this.prefixMemory.Length == 0)
            {
                // No payload was actually written, and we never requested memory, so just write it out.
                this.innerWriter.Write(prefix);
            }
            else
            {
                // Payload has been written, so write in the prefix then commit the payload.
                prefix.CopyTo(this.prefixMemory.Span);
                this.innerWriter.Advance(prefix.Length + this.advanced);
                if (this.privateWriter != null)
                {
                    // Try to minimize segments in the target writer by hinting at the total size.
                    this.innerWriter.GetSpan((int)this.privateWriter.Length);
                    foreach (var segment in this.privateWriter.AsReadOnlySequence)
                    {
                        this.innerWriter.Write(segment.Span);
                    }
                }
            }
        }

        /// <summary>
        /// Makes the initial call to acquire memory from the underlying writer if it has not been done already.
        /// </summary>
        /// <param name="sizeHint">The size requested by the caller to either <see cref="GetMemory(int)"/> or <see cref="GetSpan(int)"/>.</param>
        private void EnsureInitialized(int sizeHint)
        {
            if (this.prefixMemory.Length == 0)
            {
                int sizeToRequest = this.expectedPrefixSize + Math.Max(sizeHint, this.payloadSizeHint);
                var memory = this.innerWriter.GetMemory(sizeToRequest);
                this.prefixMemory = memory.Slice(0, this.expectedPrefixSize);
                this.realMemory = memory.Slice(this.expectedPrefixSize);
            }
        }
    }
}
