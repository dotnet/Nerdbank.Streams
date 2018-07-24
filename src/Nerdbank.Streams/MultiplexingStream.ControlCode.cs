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
            Offer,

            /// <summary>
            /// A channel proposal has been accepted.
            /// </summary>
            OfferAccepted,

            /// <summary>
            /// The payload of the frame is a payload intended for channel consumption.
            /// </summary>
            Content,

            /// <summary>
            /// Sent after all bytes have been transmitted on a given channel. Either or both sides may send this.
            /// A channel may be automatically closed when each side has both transmitted and received this message.
            /// </summary>
            ContentWritingCompleted,

            /// <summary>
            /// Sent when a channel is closed, an incoming offer is rejected, or an outgoing offer is canceled.
            /// </summary>
            ChannelTerminated,
        }
    }
}
