export interface ChannelOptions {
    /** The number of received bytes that may be buffered locally per channel (transmitted from the remote party but not yet processed). */
    channelReceivingWindowSize?: number;
}
