export interface QualifiedChannelId {
    /**  Gets the channel ID. */
    readonly id: number;

    /** Gets a value indicating whether <see cref="Id"/> is referring to a channel that was originally offered by the local party. */
    readonly offeredLocally: boolean;
}

export class QualifiedChannelId {
    static toString(id: QualifiedChannelId): string {
        return `${id.id} (${id.offeredLocally ? "local" : "remote"})`;
    }
}
