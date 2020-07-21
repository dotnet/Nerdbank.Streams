export interface OfferParameters {
    /** The name of the channel. */
    name: string;

    /** The maximum number of bytes that may be transmitted and not yet acknowledged as processed by the remote party. */
    remoteWindowSize?: number;
}
