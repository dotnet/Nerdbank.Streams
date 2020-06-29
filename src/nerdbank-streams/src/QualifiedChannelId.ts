export interface QualifiedChannelId {
    /**  Gets the channel ID. */
    readonly id: number;

    /** Gets a value indicating where the channel originated. */
    readonly source: ChannelSource;
}

/**
 * An enumeration of the possible sources of a channel.
 * @description The ordinal values are chosen so as to make flipping the perspective as easy as negating the value,
 * while leaving the Seeded value unchanged.
 */
export enum ChannelSource {
    /** The channel was offered by this MultiplexingStream instance to the other party. */
    Local = 1,

    /** The channel was offered to this MultiplexingStream instance by the other party. */
    Remote = -1,

    /**
     * The channel was seeded during construction via the Options.SeededChannels collection.
     * This channel is to be accepted by both parties.
     */
    Seeded = 0,
}

// tslint:disable-next-line: no-namespace
export namespace QualifiedChannelId {
    export function toString(id: QualifiedChannelId): string {
        const sourceName =
            id.source === ChannelSource.Local ? "local" :
            id.source === ChannelSource.Remote ? "remote" :
            id.source === ChannelSource.Seeded ? "seeded" :
            "unknown";
        return `${id.id} (${sourceName})`;
    }
}
