import { ChannelOptions } from "./ChannelOptions";

export interface MultiplexingStreamOptions {
    /**
     * The protocol version to be used.
     * @description 1 is the original version. 2 is a protocol breaking change and adds backpressure support. 3 is a protocol breaking change, eliminates the handshake packet and adds seeded channels support.
     */
    protocolMajorVersion?: number;

    /** The number of received bytes that may be buffered locally per channel (transmitted from the remote party but not yet processed). */
    defaultChannelReceivingWindowSize?: number;

    /**
     * A list of options for channels that are to be "seeded" into a new MultiplexingStream.
     * @description Seeded channels avoid the need for a round-trip for an offer/accept packet exchange.
     * Seeded channels are accessed within the MultiplexingStream instance by calling AcceptChannel(ulong, ChannelOptions?)
     * with the 0-based index into this list used as the channel ID.
     * They are only supported when ProtocolMajorVersion is at least 3.
     */
    seededChannels?: ChannelOptions[];
}
