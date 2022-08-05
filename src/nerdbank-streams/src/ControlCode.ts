/**
 * Signals what kind of frame is being transmitted.
 */
export enum ControlCode {
    /**
     * A channel is proposed to the remote party.
     */
    Offer,

    /**
     * A channel proposal has been accepted.
     */
    OfferAccepted,

    /**
     * The payload of the frame is a payload intended for channel consumption.
     */
    Content,

    /**
     * Sent after all bytes have been transmitted on a given channel. Either or both sides may send this.
     * A channel may be automatically closed when each side has both transmitted and received this message.
     */
    ContentWritingCompleted,

    /**
     * Sent when a channel is closed, an incoming offer is rejected, or an outgoing offer is canceled.
     */
    ChannelTerminated,

    /**
     * Sent when a channel has finished processing data received from the remote party, allowing them to send more data.
     */
    ContentProcessed,
}
