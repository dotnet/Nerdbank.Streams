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

            internal static unsafe int Deserialize(ArraySegment<byte> buffer, out FrameHeader header)
            {
                Requires.Argument(buffer.Count >= HeaderLength, nameof(buffer), "Buffer must be at least as long as the header.");
                header = default;
                fixed (byte* pBuffer = &buffer.Array[buffer.Offset])
                {
                    byte* ptr = pBuffer;
                    header.Code = (ControlCode)(*ptr++);
                    header.ChannelId = ReadInt32(ref ptr);
                    header.FramePayloadLength = ReadInt16(ref ptr);

                    Assumes.True(ptr - pBuffer <= HeaderLength);
                    return (int)(ptr - pBuffer);
                }
            }

            internal unsafe int Serialize(ArraySegment<byte> buffer)
            {
                Requires.Argument(buffer.Count >= HeaderLength, nameof(buffer), "Buffer must be at least as long as the header.");
                fixed (byte* pBuffer = &buffer.Array[buffer.Offset])
                {
                    byte* ptr = pBuffer;
                    *ptr++ = (byte)this.Code;
                    WriteInt32(ref ptr, this.ChannelId);
                    WriteInt16(ref ptr, this.FramePayloadLength);

                    Assumes.True(ptr - pBuffer <= HeaderLength);
                    return (int)(ptr - pBuffer);
                }
            }

            private static unsafe int ReadInt32(ref byte* ptr)
            {
                int local = 0;
                local |= *ptr++;
                local <<= 8;
                local |= *ptr++;
                local <<= 8;
                local |= *ptr++;
                local <<= 8;
                local |= *ptr++;
                return local;
            }

            private static unsafe void WriteInt32(ref byte* ptr, int value)
            {
                unchecked
                {
                    *ptr++ = (byte)(value >> 24);
                    *ptr++ = (byte)(value >> 16);
                    *ptr++ = (byte)(value >> 8);
                    *ptr++ = (byte)value;
                }
            }

            private static unsafe int ReadInt16(ref byte* ptr)
            {
                int local = 0;
                local |= *ptr++;
                local <<= 8;
                local |= *ptr++;
                return local;
            }

            private static unsafe void WriteInt16(ref byte* ptr, int value)
            {
                unchecked
                {
                    *ptr++ = (byte)(value >> 8);
                    *ptr++ = (byte)value;
                }
            }
        }
    }
}
