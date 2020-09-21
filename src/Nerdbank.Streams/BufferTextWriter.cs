// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// A <see cref="TextWriter"/> that writes to a reassignable instance of <see cref="IBufferWriter{T}"/>.
    /// </summary>
    /// <remarks>
    /// Using this is much more memory efficient than a <see cref="StreamWriter"/> when writing to many different
    /// <see cref="IBufferWriter{T}"/> because the same writer, with all its buffers, can be reused.
    /// </remarks>
    public class BufferTextWriter : TextWriter
    {
        /// <summary>
        /// A buffer of written characters that have not yet been encoded.
        /// The <see cref="charBufferPosition"/> field tracks how many characters are represented in this buffer.
        /// </summary>
        private readonly char[] charBuffer = new char[512];

        /// <summary>
        /// The internal buffer writer to use for writing encoded characters.
        /// </summary>
        private IBufferWriter<byte>? bufferWriter;

        /// <summary>
        /// The last buffer received from <see cref="bufferWriter"/>.
        /// </summary>
        private Memory<byte> memory;

        /// <summary>
        /// The number of characters written to the <see cref="memory"/> buffer.
        /// </summary>
        private int memoryPosition;

        /// <summary>
        /// The number of characters written to the <see cref="charBuffer"/>.
        /// </summary>
        private int charBufferPosition;

        /// <summary>
        /// Whether the encoding preamble has been written since the last call to <see cref="Initialize(IBufferWriter{byte}, Encoding)"/>.
        /// </summary>
        private bool preambleWritten;

        /// <summary>
        /// The encoding currently in use.
        /// </summary>
        private Encoding? encoding;

        /// <summary>
        /// The preamble for the current <see cref="encoding"/>.
        /// </summary>
        /// <remarks>
        /// We store this as a field to avoid calling <see cref="Encoding.GetPreamble"/> repeatedly,
        /// since the typical implementation allocates a new array for each call.
        /// </remarks>
        private ReadOnlyMemory<byte> encodingPreamble;

        /// <summary>
        /// An encoder obtained from the current <see cref="encoding"/> used for incrementally encoding written characters.
        /// </summary>
        private Encoder? encoder;

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferTextWriter"/> class.
        /// </summary>
        /// <remarks>
        /// When using this constructor, call <see cref="Initialize(IBufferWriter{byte}, Encoding)"/>
        /// to associate the instance with the initial writer to use before using any write or flush methods.
        /// </remarks>
        public BufferTextWriter()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferTextWriter"/> class.
        /// </summary>
        /// <param name="bufferWriter">The buffer writer to write to.</param>
        /// <param name="encoding">The encoding to use.</param>
        public BufferTextWriter(IBufferWriter<byte> bufferWriter, Encoding encoding)
        {
            this.Initialize(bufferWriter, encoding);
        }

        /// <inheritdoc />
        public override Encoding Encoding => this.encoding ?? throw new InvalidOperationException("Call " + nameof(this.Initialize) + " first.");

        /// <summary>
        /// Gets the number of uninitialized characters remaining in <see cref="charBuffer"/>.
        /// </summary>
        private int CharBufferSlack => this.charBuffer.Length - this.charBufferPosition;

        /// <summary>
        /// Prepares for writing to the specified buffer.
        /// </summary>
        /// <param name="bufferWriter">The buffer writer to write to.</param>
        /// <param name="encoding">The encoding to use.</param>
        public void Initialize(IBufferWriter<byte> bufferWriter, Encoding encoding)
        {
            Requires.NotNull(bufferWriter, nameof(bufferWriter));
            Requires.NotNull(encoding, nameof(encoding));

            Verify.Operation(this.memoryPosition == 0 && this.charBufferPosition == 0, "This instance must be flushed before being reinitialized.");

            this.preambleWritten = false;
            this.bufferWriter = bufferWriter;
            if (encoding != this.encoding)
            {
                this.encoding = encoding;
                this.encoder = this.encoding.GetEncoder();
                this.encodingPreamble = this.encoding.GetPreamble();
            }
            else
            {
                // this.encoder != null because if it were, this.encoding == null too, so we would have been in the first branch above.
                this.encoder!.Reset();
            }
        }

        /// <summary>
        /// Clears references to the <see cref="IBufferWriter{T}"/> set by a prior call to <see cref="Initialize(IBufferWriter{byte}, Encoding)"/>.
        /// </summary>
        public void Reset()
        {
            this.bufferWriter = null;
        }

        /// <inheritdoc />
        public override void Flush()
        {
            this.ThrowIfNotInitialized();
            this.EncodeCharacters(flushEncoder: true);
            this.CommitBytes();
        }

        /// <inheritdoc />
        public override Task FlushAsync()
        {
            try
            {
                this.Flush();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        /// <inheritdoc />
        public override void Write(char value)
        {
            this.ThrowIfNotInitialized();
            this.charBuffer[this.charBufferPosition++] = value;
            this.EncodeCharactersIfBufferFull();
        }

        /// <inheritdoc />
        public override void Write(string? value)
        {
            if (value == null)
            {
                return;
            }

            this.Write(value.AsSpan());
        }

        /// <inheritdoc />
        public override void Write(char[] buffer, int index, int count) => this.Write(Requires.NotNull(buffer, nameof(buffer)).AsSpan(index, count));

#if SPAN_BUILTIN
        /// <inheritdoc />
        public override void Write(ReadOnlySpan<char> buffer)
#else
        /// <summary>
        /// Copies a given span of characters into the writer.
        /// </summary>
        /// <param name="buffer">The characters to write.</param>
        public virtual void Write(ReadOnlySpan<char> buffer)
#endif
        {
            this.ThrowIfNotInitialized();

            // Try for fast path
            if (buffer.Length <= this.CharBufferSlack)
            {
                buffer.CopyTo(this.charBuffer.AsSpan(this.charBufferPosition));
                this.charBufferPosition += buffer.Length;
                this.EncodeCharactersIfBufferFull();
            }
            else
            {
                int charsCopied = 0;
                while (charsCopied < buffer.Length)
                {
                    int charsToCopy = Math.Min(buffer.Length - charsCopied, this.CharBufferSlack);
                    buffer.Slice(charsCopied, charsToCopy).CopyTo(this.charBuffer.AsSpan(this.charBufferPosition));
                    charsCopied += charsToCopy;
                    this.charBufferPosition += charsToCopy;
                    this.EncodeCharactersIfBufferFull();
                }
            }
        }

#if SPAN_BUILTIN
        /// <inheritdoc />
        public override void WriteLine(ReadOnlySpan<char> buffer)
#else
        /// <summary>
        /// Writes a span of characters followed by a <see cref="TextWriter.NewLine"/>.
        /// </summary>
        /// <param name="buffer">The characters to write.</param>
        public virtual void WriteLine(ReadOnlySpan<char> buffer)
#endif
        {
            this.Write(buffer);
            this.WriteLine();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.bufferWriter is object)
                {
                    this.Flush();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Encodes the written characters if the character buffer is full.
        /// </summary>
        private void EncodeCharactersIfBufferFull()
        {
            if (this.charBufferPosition == this.charBuffer.Length)
            {
                this.EncodeCharacters(flushEncoder: false);
            }
        }

        /// <summary>
        /// Encodes characters written so far to a buffer provided by the underyling <see cref="bufferWriter"/>.
        /// </summary>
        /// <param name="flushEncoder"><c>true</c> to flush the characters in the encoder; useful when finalizing the output.</param>
        private void EncodeCharacters(bool flushEncoder)
        {
            if (this.charBufferPosition > 0)
            {
                int maxBytesLength = this.Encoding!.GetMaxByteCount(this.charBufferPosition);
                if (!this.preambleWritten)
                {
                    maxBytesLength += this.encodingPreamble.Length;
                }

                if (this.memory.Length - this.memoryPosition < maxBytesLength)
                {
                    this.CommitBytes();
                    this.memory = this.bufferWriter!.GetMemory(maxBytesLength);
                }

                if (!this.preambleWritten)
                {
                    this.encodingPreamble.Span.CopyTo(this.memory.Span.Slice(this.memoryPosition));
                    this.memoryPosition += this.encodingPreamble.Length;
                    this.preambleWritten = true;
                }

                if (MemoryMarshal.TryGetArray(this.memory, out ArraySegment<byte> segment))
                {
                    this.memoryPosition += this.encoder!.GetBytes(this.charBuffer, 0, this.charBufferPosition, segment.Array!, segment.Offset + this.memoryPosition, flush: flushEncoder);
                }
                else
                {
                    byte[] rentedByteBuffer = ArrayPool<byte>.Shared.Rent(maxBytesLength);
                    try
                    {
                        int bytesWritten = this.encoder!.GetBytes(this.charBuffer, 0, this.charBufferPosition, rentedByteBuffer, 0, flush: flushEncoder);
                        rentedByteBuffer.CopyTo(this.memory.Span.Slice(this.memoryPosition));
                        this.memoryPosition += bytesWritten;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rentedByteBuffer);
                    }
                }

                this.charBufferPosition = 0;

                if (this.memoryPosition == this.memory.Length)
                {
                    this.Flush();
                }
            }
        }

        /// <summary>
        /// Commits any written bytes to the underlying <see cref="bufferWriter"/>.
        /// </summary>
        private void CommitBytes()
        {
            if (this.memoryPosition > 0)
            {
                this.bufferWriter!.Advance(this.memoryPosition);
                this.memoryPosition = 0;
                this.memory = default;
            }
        }

        private void ThrowIfNotInitialized()
        {
            if (this.bufferWriter == null)
            {
                throw new InvalidOperationException("Call " + nameof(this.Initialize) + " first.");
            }
        }
    }
}
