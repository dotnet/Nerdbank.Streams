// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    /// <content>
    /// Contains the <see cref="ControlCode"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        /// <summary>
        /// Signals what kind of frame is being transmitted.
        /// </summary>
        private enum ControlCode : byte
        {
            /// <summary>
            /// A channel is proposed to the remote party.
            /// </summary>
            CreatingChannel,

            /// <summary>
            /// A previous channel proposal is rescinded.
            /// </summary>
            CreatingChannelCanceled,

            /// <summary>
            /// A channel proposal has been accepted.
            /// </summary>
            ChannelCreated,

            /// <summary>
            /// The payload of the frame is a payload intended for channel consumption.
            /// </summary>
            Content,

            /// <summary>
            /// Sent when a channel is disposed of.
            /// </summary>
            ChannelTerminated,
        }
    }
}
