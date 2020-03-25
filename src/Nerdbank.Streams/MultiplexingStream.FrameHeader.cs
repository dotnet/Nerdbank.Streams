// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Diagnostics;
    using Microsoft;

    /// <content>
    /// Contains the <see cref="FrameHeader"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
        internal struct FrameHeader
        {
            /// <summary>
            /// Gets or sets the kind of frame this is.
            /// </summary>
            internal ControlCode Code { get; set; }

            /// <summary>
            /// Gets or sets the channel that this frame refers to or carries a payload for.
            /// </summary>
            internal int? ChannelId { get; set; }

            /// <summary>
            /// Gets the ID of the channel that this frame refers to or carries a payload for.
            /// </summary>
            /// <exception cref="MultiplexingProtocolException">Thrown if <see cref="ChannelId"/> is null.</exception>
            internal int RequiredChannelId => this.ChannelId ?? throw new MultiplexingProtocolException("Expected ChannelId not present in frame header.");

            /// <summary>
            /// Gets the text to display in the debugger when an instance of this struct is displayed.
            /// </summary>
            private string DebuggerDisplay => $"{this.Code} {this.ChannelId}";
        }
    }
}
