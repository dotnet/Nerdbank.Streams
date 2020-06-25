import { ControlCode } from "./ControlCode";
import { requireInteger } from "./Utilities";

export class FrameHeader {
    /**
     * Gets the kind of frame this is.
     */
    public readonly code: ControlCode;

    /**
     * Gets the channel that this frame refers to or carries a payload for.
     */
    public readonly channelId?: number;

    /**
     * Gets the channel that this frame refers to or carries a payload for.
     */
    public get requiredChannelId(): number {
        if (this.channelId) {
            return this.channelId;
        }

        throw new Error("Expected ChannelId not present in frame header.");
    }

    /**
     * Initializes a new instance of the `FrameHeader` class.
     * @param code the kind of frame this is.
     * @param channelId the channel that this frame refers to or carries a payload for.
     */
    constructor(code: ControlCode, channelId?: number) {
        if (channelId) {
            requireInteger("channelId", channelId, 4, "signed");
        }

        this.code = code;
        this.channelId = channelId;
    }
}
