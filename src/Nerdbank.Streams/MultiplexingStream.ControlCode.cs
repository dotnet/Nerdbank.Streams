// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        internal enum ControlCode : byte
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

            /// <summary>
            /// Sent when a channel has finished processing data received from the remote party,
            /// allowing them to send more data.
            /// </summary>
            ContentProcessed,

            /// <summary>
            /// Sent when a channel reader has completed and can no longer receive content,
            /// so the remote party can stop transmitting and release any writer blocked on flow control.
            /// </summary>
            /// <remarks>
            /// Only sent when the protocol version supports backpressure (v2 and later).
            /// Recipients that do not recognize this code ignore it.
            /// </remarks>
            ContentReadingCompleted,

            /// <summary>
            /// Sent by a channel's receiver to enlarge the receiving window it previously advertised,
            /// permitting the remote party to have more unacknowledged bytes in flight.
            /// </summary>
            /// <remarks>
            /// The payload carries the new (absolute) window size, which is only ever larger than the prior value.
            /// This code is additive to the protocol rather than a new version of it: recipients that predate it
            /// ignore it, which is safe because the window they already agreed to remains valid.
            /// It is only ever sent in reply to a <see cref="ChannelWindowGrowthRequest"/>, so it is never sent
            /// to a party that would not understand it.
            /// </remarks>
            ChannelWindowAdjust,

            /// <summary>
            /// Sent by a channel's sender when it has run out of credit and still has data to send,
            /// asking the receiver to enlarge the window it advertises.
            /// </summary>
            /// <remarks>
            /// This code is additive to the protocol rather than a new version of it. A receiver that predates it
            /// ignores it and never replies, which costs one small frame per channel and leaves throughput
            /// exactly where a fixed window would have left it.
            /// </remarks>
            ChannelWindowGrowthRequest,
        }
    }
}
