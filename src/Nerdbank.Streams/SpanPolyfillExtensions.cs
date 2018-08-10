// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

#pragma warning disable AvoidAsyncSuffix // Avoid Async suffix
#if !SPAN_BUILTIN

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipelines;
    using System.Net.WebSockets;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    internal static class SpanPolyfillExtensions
    {
        /// <summary>
        /// Reads from the stream into a memory buffer.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="buffer">The buffer to read directly into.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The number of bytes actually read.</returns>
        /// <devremarks>
        /// This method shamelessly copied from the .NET Core 2.1 Stream class: https://github.com/dotnet/coreclr/blob/a113b1c803783c9d64f1f0e946ff9a853e3bc140/src/System.Private.CoreLib/shared/System/IO/Stream.cs#L366-L391.
        /// </devremarks>
        internal static ValueTask<int> ReadAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));

            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
            {
                return new ValueTask<int>(stream.ReadAsync(array.Array, array.Offset, array.Count, cancellationToken));
            }
            else
            {
                byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                return FinishReadAsync(stream.ReadAsync(sharedBuffer, 0, buffer.Length, cancellationToken), sharedBuffer, buffer);

                async ValueTask<int> FinishReadAsync(Task<int> readTask, byte[] localBuffer, Memory<byte> localDestination)
                {
                    try
                    {
                        int result = await readTask.ConfigureAwait(false);
                        new Span<byte>(localBuffer, 0, result).CopyTo(localDestination.Span);
                        return result;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(localBuffer);
                    }
                }
            }
        }

        /// <summary>
        /// Writes to a stream from a memory buffer.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="buffer">The buffer to read from.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that indicates when the write operation is complete.</returns>
        /// <devremarks>
        /// This method shamelessly copied from the .NET Core 2.1 Stream class: https://github.com/dotnet/coreclr/blob/a113b1c803783c9d64f1f0e946ff9a853e3bc140/src/System.Private.CoreLib/shared/System/IO/Stream.cs#L672-L696.
        /// </devremarks>
        internal static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));

            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
            {
                return new ValueTask(stream.WriteAsync(array.Array, array.Offset, array.Count, cancellationToken));
            }
            else
            {
                byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                buffer.Span.CopyTo(sharedBuffer);
                return new ValueTask(FinishWriteAsync(stream.WriteAsync(sharedBuffer, 0, buffer.Length, cancellationToken), sharedBuffer));
            }

            async Task FinishWriteAsync(Task writeTask, byte[] localBuffer)
            {
                try
                {
                    await writeTask.ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(localBuffer);
                }
            }
        }

        /// <summary>
        /// Reads from the stream into a memory buffer.
        /// </summary>
        /// <param name="webSocket">The stream to read from.</param>
        /// <param name="buffer">The buffer to read directly into.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The number of bytes actually read.</returns>
        /// <devremarks>
        /// This method shamelessly copied from the .NET Core 2.1 Stream class: https://github.com/dotnet/coreclr/blob/a113b1c803783c9d64f1f0e946ff9a853e3bc140/src/System.Private.CoreLib/shared/System/IO/Stream.cs#L366-L391.
        /// </devremarks>
        internal static ValueTask<WebSocketReceiveResult> ReceiveAsync(this WebSocket webSocket, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(webSocket, nameof(webSocket));

            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
            {
                return new ValueTask<WebSocketReceiveResult>(webSocket.ReceiveAsync(array, cancellationToken));
            }
            else
            {
                byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                return FinishReadAsync(webSocket.ReceiveAsync(new ArraySegment<byte>(sharedBuffer, 0, buffer.Length), cancellationToken), sharedBuffer, buffer);

                async ValueTask<WebSocketReceiveResult> FinishReadAsync(Task<WebSocketReceiveResult> readTask, byte[] localBuffer, Memory<byte> localDestination)
                {
                    try
                    {
                        WebSocketReceiveResult result = await readTask.ConfigureAwait(false);
                        new Span<byte>(localBuffer, 0, result.Count).CopyTo(localDestination.Span);
                        return result;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(localBuffer);
                    }
                }
            }
        }

        /// <summary>
        /// Writes to a stream from a memory buffer.
        /// </summary>
        /// <param name="webSocket">The stream to write to.</param>
        /// <param name="buffer">The buffer to read from.</param>
        /// <param name="messageType">The type of WebSocket message.</param>
        /// <param name="endOfMessage">Whether to signify that this write operation concludes a "message" in the semantic of whatever protocol is being used.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that indicates when the write operation is complete.</returns>
        /// <devremarks>
        /// This method shamelessly copied from the .NET Core 2.1 Stream class: https://github.com/dotnet/coreclr/blob/a113b1c803783c9d64f1f0e946ff9a853e3bc140/src/System.Private.CoreLib/shared/System/IO/Stream.cs#L672-L696.
        /// </devremarks>
        internal static ValueTask SendAsync(this WebSocket webSocket, ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(webSocket, nameof(webSocket));

            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
            {
                return new ValueTask(webSocket.SendAsync(array, messageType, endOfMessage, cancellationToken));
            }
            else
            {
                byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                buffer.Span.CopyTo(sharedBuffer);
                return new ValueTask(FinishWriteAsync(webSocket.SendAsync(new ArraySegment<byte>(sharedBuffer, 0, buffer.Length), messageType, endOfMessage, cancellationToken), sharedBuffer));
            }

            async Task FinishWriteAsync(Task writeTask, byte[] localBuffer)
            {
                try
                {
                    await writeTask.ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(localBuffer);
                }
            }
        }
    }
}

#endif
