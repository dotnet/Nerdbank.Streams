// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System.IO;
    using System.Net.WebSockets;

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
    }
}
