// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MessagePack;

using System.IO;

internal partial class MessagePackStreamReader
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessagePackStreamReader"/> class with a workload-specific minimum buffer size.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open when the reader is disposed.</param>
    /// <param name="minimumSpanLength">The minimum size of the sequence's read buffers.</param>
    internal MessagePackStreamReader(Stream stream, bool leaveOpen, int minimumSpanLength)
        : this(stream, leaveOpen)
    {
        this.ReadData.MinimumSpanLength = minimumSpanLength;
    }
}
