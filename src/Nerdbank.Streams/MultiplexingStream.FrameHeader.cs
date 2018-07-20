// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using Microsoft;

    /// <content>
    /// Contains the <see cref="FrameHeader"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        private struct FrameHeader
        {
            internal static int HeaderLength => sizeof(ControlCode) + sizeof(int) + sizeof(short);

            /// <summary>
            /// Gets or sets the kind of frame this is.
            /// </summary>
            internal ControlCode Code { get; set; }

            /// <summary>
            /// Gets or sets the channel that this frame refers to or carries a payload for.
            /// </summary>
            internal int ChannelId { get; set; }

            /// <summary>
            /// Gets or sets the length of the frame content (excluding the header).
            /// </summary>
            /// <remarks>
            /// Must be no greater than <see cref="ushort.MaxValue"/>.
            /// </remarks>
            internal int FramePayloadLength { get; set; }

            internal static FrameHeader Deserialize(ReadOnlySpan<byte> buffer)
            {
                Requires.Argument(buffer.Length == HeaderLength, nameof(buffer), "Buffer must be header length.");
                return new FrameHeader
                {
                    Code = (ControlCode)buffer[0],
                    ChannelId = ReadInt(buffer.Slice(1, 4)),
                    FramePayloadLength = ReadInt(buffer.Slice(5, 2)),
                };
            }

            internal void Serialize(Span<byte> buffer)
            {
                Requires.Argument(buffer.Length == HeaderLength, nameof(buffer), "Buffer must be header length.");
                buffer[0] = (byte)this.Code;
                Write(buffer.Slice(1, 4), this.ChannelId);
                Write(buffer.Slice(5, 2), (ushort)this.FramePayloadLength);
            }

            private static int ReadInt(ReadOnlySpan<byte> buffer)
            {
                Requires.Argument(buffer.Length <= 4, nameof(buffer), "Int32 length exceeded.");

                int local = 0;
                for (int offset = 0; offset < buffer.Length; offset++)
                {
                    local <<= 8;
                    local |= buffer[offset];
                }

                return local;
            }

            private static void Write(Span<byte> buffer, int value)
            {
                buffer[0] = (byte)(value >> 24);
                buffer[1] = (byte)(value >> 16);
                buffer[2] = (byte)(value >> 8);
                buffer[3] = (byte)value;
            }

            private static void Write(Span<byte> buffer, ushort value)
            {
                buffer[0] = (byte)(value >> 8);
                buffer[1] = (byte)value;
            }
        }
    }
}
