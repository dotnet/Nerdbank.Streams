import { ControlCode } from "./ControlCode";
import { requireInteger } from "./Utilities";

export class FrameHeader {
    public static readonly HeaderLength = 1 /*control code*/ + 4 /*channel id*/ + 2/*payload length*/;

    public static Deserialize(buffer: Buffer): FrameHeader {
        if (!buffer || buffer.length !== FrameHeader.HeaderLength) {
            throw new Error("buffer must have length of " + FrameHeader.HeaderLength);
        }

        const code: ControlCode = buffer[0];
        const channelId = buffer.readUInt32BE(1);
        const framePayloadLength = buffer.readUInt16BE(5);
        const header = new FrameHeader(code, channelId, framePayloadLength);
        return header;
    }

    /**
     * Gets the kind of frame this is.
     */
    public readonly code: ControlCode;

    /**
     * Gets the channel that this frame refers to or carries a payload for.
     */
    public readonly channelId: number;

    /**
     * Gets the length of the frame content (excluding the header).
     * Must be no greater than 65535 (uint16 max value).
     */
    public readonly framePayloadLength: number;

    /**
     * Initializes a new instance of the `FrameHeader` class.
     * @param code the kind of frame this is.
     * @param channelId the channel that this frame refers to or carries a payload for.
     * @param framePayloadLength the length of the frame content (excluding the header).
     * Must be no greater than 65535 (uint16 max value).
     */
    constructor(code: ControlCode, channelId: number, framePayloadLength: number = 0) {
        requireInteger("channelId", channelId, 4, "signed");
        requireInteger("framePayloadLength", framePayloadLength, 2, "unsigned");

        this.code = code;
        this.channelId = channelId;
        this.framePayloadLength = framePayloadLength || 0;
    }

    public serialize(buffer: Buffer) {
        if (!buffer || buffer.length !== FrameHeader.HeaderLength) {
            throw new Error("buffer must have length of " + FrameHeader.HeaderLength);
        }

        buffer[0] = this.code;
        buffer.writeUInt32BE(this.channelId, 1);
        buffer.writeUInt16BE(this.framePayloadLength, 5);
    }
}
