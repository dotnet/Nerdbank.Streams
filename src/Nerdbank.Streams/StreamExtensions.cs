// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// Stream extension methods.
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// Creates a <see cref="Stream"/> that can read no more than a given number of bytes from an underlying stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="length">The number of bytes to read from the parent stream.</param>
        /// <returns>A stream that ends after <paramref name="length"/> bytes are read.</returns>
        public static Stream ReadSlice(this Stream stream, long length) => new NestedStream(stream, length);

        /// <summary>
        /// Exposes a <see cref="WebSocket"/> as a <see cref="Stream"/>.
        /// </summary>
        /// <param name="webSocket">The <see cref="WebSocket"/> to use as a transport for the returned <see cref="Stream"/>.</param>
        /// <returns>A bidirectional <see cref="Stream"/>.</returns>
        public static Stream AsStream(this WebSocket webSocket) => new WebSocketStream(webSocket);

        /// <summary>
        /// Exposes a <see cref="ReadOnlySequence{T}"/> of <see cref="byte"/> as a <see cref="Stream"/>.
        /// </summary>
        /// <param name="readOnlySequence">The sequence of bytes to expose as a stream.</param>
        /// <returns>The readable stream.</returns>
        public static Stream AsStream(this ReadOnlySequence<byte> readOnlySequence) => new ReadOnlySequenceStream(readOnlySequence);

        /// <summary>
        /// Creates a writable <see cref="Stream"/> that can be used to add to a <see cref="IBufferWriter{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="writer">The buffer writer the stream should write to.</param>
        /// <returns>A <see cref="Stream"/>.</returns>
        public static Stream AsStream(this IBufferWriter<byte> writer) => new BufferWriterStream(writer);

        /// <summary>
        /// Create a new <see cref="StreamWriter"/> that can be used to write a byte sequence of undetermined length to some underlying <see cref="Stream"/>,
        /// such that it can later be read back as if it were a <see cref="Stream"/> of its own that ends at the end of this particular sequence.
        /// </summary>
        /// <param name="stream">The underlying stream to write to.</param>
        /// <param name="minimumBufferSize">The buffer size to use.</param>
        /// <returns>The new <see cref="Stream"/>.</returns>
        /// <remarks>
        /// Write to the returned <see cref="Stream"/> until the sub-stream is complete. Call <see cref="Substream.DisposeAsync(System.Threading.CancellationToken)"/>
        /// when done and resume writing to the parent stream as needed.
        /// </remarks>
        public static Substream WriteSubstream(this Stream stream, int minimumBufferSize = Substream.DefaultBufferSize) => new Substream(stream, minimumBufferSize);

        /// <summary>
        /// Create a new <see cref="StreamReader"/> that will read a sequence previously written to this stream using <see cref="WriteSubstream"/>.
        /// </summary>
        /// <param name="stream">The underlying stream to read from.</param>
        /// <returns>A stream that will read just to the end of the substream and then end.</returns>
        public static Stream ReadSubstream(this Stream stream) => new SubstreamReader(stream);

        /// <summary>
        /// Fills a given buffer with bytes from the specified <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="buffer">The buffer to fill from the <paramref name="stream"/>.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A task that represents the asynchronous read operation. Its resulting value contains the total number of bytes read into the buffer.
        /// The result value can be less than the length of the given buffer if the end of the stream has been reached.
        /// </returns>
        /// <exception cref="OperationCanceledException">May be thrown if <paramref name="cancellationToken"/> is canceled before reading has completed.</exception>
        /// <remarks>
        /// The returned task does not complete until either the <paramref name="buffer"/> is filled or the end of the <paramref name="stream"/> has been reached.
        /// </remarks>
        public static async ValueTask<int> ReadBlockAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));

            int totalBytesRead = 0;
            while (buffer.Length > totalBytesRead)
            {
                int bytesJustRead = await stream.ReadAsync(buffer.Slice(totalBytesRead), cancellationToken).ConfigureAwait(false);
                totalBytesRead += bytesJustRead;
                if (bytesJustRead == 0)
                {
                    // We've reached the end of the stream.
                    break;
                }
            }

            return totalBytesRead;
        }

        /// <summary>
        /// Fills a given buffer with bytes from the specified <see cref="Stream"/>
        /// or throws if the end of the stream is reached first.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="buffer">The buffer to fill from the <paramref name="stream"/>.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A task that represents the asynchronous read operation.
        /// </returns>
        /// <exception cref="OperationCanceledException">May be thrown if <paramref name="cancellationToken"/> is canceled before reading has completed.</exception>
        /// <exception cref="EndOfStreamException">Thrown if the end of the stream is encountered before filling the buffer.</exception>
        /// <remarks>
        /// The returned task does not complete until either the <paramref name="buffer"/> is filled or the end of the <paramref name="stream"/> has been reached.
        /// </remarks>
        public static async ValueTask ReadBlockOrThrowAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int bytesRead = await ReadBlockAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead < buffer.Length)
            {
                throw new EndOfStreamException($"Expected {buffer.Length} bytes but only received {bytesRead} before the stream ended.");
            }
        }
    }
}
