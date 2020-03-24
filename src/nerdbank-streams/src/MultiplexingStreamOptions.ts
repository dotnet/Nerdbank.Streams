export class MultiplexingStreamOptions {
    /**
     * The protocol version to be used.
     * @description 1 is the original version. 2 is a protocol breaking change and adds backpressure support.
     */
    protocolMajorVersion?: number;

    /** The number of received bytes that may be buffered locally per channel (transmitted from the remote party but not yet processed). */
    defaultChannelReceivingWindowSize?: number;
}
