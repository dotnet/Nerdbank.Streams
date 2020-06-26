import { ControlCode } from "./ControlCode";
import { requireInteger } from "./Utilities";
import { QualifiedChannelId } from "./QualifiedChannelId";

export class FrameHeader {
    /**
     * Gets the channel that this frame refers to or carries a payload for.
     */
    public get requiredChannel(): QualifiedChannelId {
        if (this.channel) {
            return this.channel;
        }

        throw new Error("Expected channel not present in frame header.");
    }

    public static createToSend(code: ControlCode, channelId?: QualifiedChannelId): FrameHeader {
        return new FrameHeader(code, channelId);
    }

    public static createFromReceived(code: ControlCode, localIsOdd: boolean | null, channel: { id: number, offeredBySender: boolean | null } | undefined): FrameHeader {
        if (!channel) {
            return new FrameHeader(code);
        }

        const channelIsOdd = channel.id % 2 === 1;
        const offeredBySender = channel.offeredBySender !== null ? channel.offeredBySender : (channelIsOdd !== localIsOdd);
        return new FrameHeader(code, { id: channel.id, offeredLocally: !offeredBySender });
    }

    /**
     * Initializes a new instance of the `FrameHeader` class.
     * @param code The kind of frame this is.
     * @param channel The channel that this frame refers to or carries a payload for.
     */
    private constructor(public readonly code: ControlCode, public readonly channel?: QualifiedChannelId) {
        if (channel) {
            requireInteger("channelId", channel.id, 4, "signed");
        }

        this.code = code;
    }
}
