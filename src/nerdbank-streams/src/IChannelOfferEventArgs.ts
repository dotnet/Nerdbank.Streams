/**
 * Describes an offer for a channel.
 */
export interface IChannelOfferEventArgs {
    /**
     * Gets the unique ID of the channel.
     */
    readonly id: number;

    /**
     * Gets the name of the channel.
     */
    readonly name: string;

    /**
     * Gets a value indicating whether the channel has already been accepted.
     */
    readonly isAccepted: boolean;
}
