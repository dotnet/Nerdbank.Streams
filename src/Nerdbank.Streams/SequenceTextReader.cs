// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// A <see cref="TextReader"/> that reads from a reassignable instance of <see cref="ReadOnlySequence{T}"/>.
    /// </summary>
    /// <remarks>
    /// Using this is much more memory efficient than a <see cref="StreamReader"/> when reading from many different
    /// <see cref="ReadOnlySequence{T}"/> instances because the same reader, with all its buffers, can be reused.
    /// </remarks>
    public class SequenceTextReader : TextReader
    {
        /// <summary>
        /// A buffer of written characters that have not yet been encoded.
        /// The <see cref="charBufferPosition"/> field tracks how many characters are represented in this buffer.
        /// </summary>
        private readonly char[] charBuffer = new char[512];

        /// <summary>
        /// The number of characters already read from <see cref="charBuffer"/>.
        /// </summary>
        private int charBufferPosition;

        /// <summary>
        /// The number of characters decoded into <see cref="charBuffer"/>.
        /// </summary>
        private int charBufferLength;

        /// <summary>
        /// The sequence to be decoded and read.
        /// </summary>
        private ReadOnlySequence<byte> sequence;

        /// <summary>
        /// The position of the next byte to decode in <see cref="sequence"/>.
        /// </summary>
        private SequencePosition sequencePosition;

        /// <summary>
        /// The encoding to use while decoding bytes into characters.
        /// </summary>
        private Encoding encoding;

        /// <summary>
        /// The decoder.
        /// </summary>
        private Decoder decoder;

        /// <summary>
        /// The preamble for the <see cref="encoding"/> in use.
        /// </summary>
        private byte[] encodingPreamble;

        /// <summary>
        /// Initializes a new instance of the <see cref="SequenceTextReader"/> class
        /// without associating it with an initial <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        /// <remarks>
        /// When using this constructor, call <see cref="Initialize(ReadOnlySequence{byte}, Encoding)"/>
        /// to associate the instance with the initial byte sequence to be read.
        /// </remarks>
        public SequenceTextReader()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SequenceTextReader"/> class.
        /// </summary>
        /// <param name="sequence">The sequence to read from.</param>
        /// <param name="encoding">The encoding to use.</param>
        public SequenceTextReader(ReadOnlySequence<byte> sequence, Encoding encoding)
        {
            this.Initialize(sequence, encoding);
        }

        /// <summary>
        /// Initializes or reinitializes this instance to read from a given <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        /// <param name="sequence">The sequence to read from.</param>
        /// <param name="encoding">The encoding to use.</param>
        public void Initialize(ReadOnlySequence<byte> sequence, Encoding encoding)
        {
            Requires.NotNull(encoding, nameof(encoding));

            this.sequence = sequence;
            this.sequencePosition = sequence.Start;

            this.charBufferPosition = 0;
            this.charBufferLength = 0;

            if (encoding != this.encoding)
            {
                this.encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
                this.decoder = this.encoding.GetDecoder();
                this.encodingPreamble = this.encoding.GetPreamble();
            }
            else
            {
                this.decoder.Reset();
            }

            // Skip a preamble if we encounter one.
            if (this.encodingPreamble.Length > 0 && sequence.Length >= this.encodingPreamble.Length)
            {
                Span<byte> provisionalRead = stackalloc byte[this.encodingPreamble.Length];
                sequence.Slice(0, this.encodingPreamble.Length).CopyTo(provisionalRead);
                bool match = true;
                for (int i = 0; match && i < this.encodingPreamble.Length; i++)
                {
                    match = this.encodingPreamble[i] == provisionalRead[i];
                }

                if (match)
                {
                    // We encountered a preamble. Skip it.
                    this.sequencePosition = this.sequence.GetPosition(this.encodingPreamble.Length, this.sequence.Start);
                }
            }
        }

        /// <summary>
        /// Clears references to the <see cref="ReadOnlySequence{T}"/> set by a prior call to <see cref="Initialize(ReadOnlySequence{byte}, Encoding)"/>.
        /// </summary>
        public void Reset()
        {
            this.sequence = default;
            this.sequencePosition = default;
        }

        /// <inheritdoc />
        public override int Peek()
        {
            this.DecodeCharsIfNecessary();
            if (this.charBufferPosition == this.charBufferLength)
            {
                return -1;
            }

            return this.charBuffer[this.charBufferPosition];
        }

        /// <inheritdoc />
        public override int Read()
        {
            int result = this.Peek();
            if (result != -1)
            {
                this.charBufferPosition++;
            }

            return result;
        }

        /// <inheritdoc />
        public override int Read(char[] buffer, int index, int count)
        {
            Utilities.ValidateBufferIndexAndCount(buffer, index, count);

            this.DecodeCharsIfNecessary();

            int copied = Math.Min(count, this.charBufferLength - this.charBufferPosition);
            Array.Copy(this.charBuffer, this.charBufferPosition, buffer, index, copied);
            this.charBufferPosition += copied;
            return copied;
        }

        /// <inheritdoc />
        public override Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            try
            {
                int result = this.Read(buffer, index, count);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

        /// <inheritdoc />
        public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
        {
            try
            {
                int result = this.ReadBlock(buffer, index, count);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

        /// <inheritdoc />
        public override Task<string> ReadToEndAsync()
        {
            try
            {
                string result = this.ReadToEnd();
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromException<string>(ex);
            }
        }

        /// <inheritdoc />
        public override Task<string> ReadLineAsync()
        {
            try
            {
                string result = this.ReadLine();
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromException<string>(ex);
            }
        }

#if SPAN_BUILTIN
        /// <inheritdoc />
        public override int Read(Span<char> buffer)
        {
            this.DecodeCharsIfNecessary();

            int copied = Math.Min(buffer.Length, this.charBufferLength - this.charBufferPosition);
            this.charBuffer.AsSpan(this.charBufferPosition, copied).CopyTo(buffer);
            this.charBufferPosition += copied;
            return copied;
        }

        /// <inheritdoc />
        public override ValueTask<int> ReadBlockAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                int result = this.ReadBlock(buffer.Span);
                return new ValueTask<int>(result);
            }
            catch (Exception ex)
            {
                return new ValueTask<int>(Task.FromException<int>(ex));
            }
        }

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                int result = this.Read(buffer.Span);
                return new ValueTask<int>(result);
            }
            catch (Exception ex)
            {
                return new ValueTask<int>(Task.FromException<int>(ex));
            }
        }
#endif

        private void DecodeCharsIfNecessary()
        {
            if (this.charBufferPosition == this.charBufferLength && !this.sequence.End.Equals(this.sequencePosition))
            {
                this.DecodeChars();
            }
        }

        private void DecodeChars()
        {
            if (this.charBufferPosition == this.charBufferLength)
            {
                // Reset to consider our character buffer empty.
                this.charBufferPosition = 0;
                this.charBufferLength = 0;
            }

            while (this.charBufferLength < this.charBuffer.Length)
            {
                Assumes.True(this.sequence.TryGet(ref this.sequencePosition, out ReadOnlyMemory<byte> memory, advance: false));
                if (memory.IsEmpty)
                {
                    this.sequencePosition = this.sequence.End;
                    break;
                }

                if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
                {
                    this.decoder.Convert(segment.Array, segment.Offset, segment.Count, this.charBuffer, this.charBufferLength, this.charBuffer.Length - this.charBufferLength, flush: false, out int bytesUsed, out int charsUsed, out bool completed);
                    this.charBufferLength += charsUsed;
                    this.sequencePosition = this.sequence.GetPosition(bytesUsed, this.sequencePosition);
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
        }
    }
}
