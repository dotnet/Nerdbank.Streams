import { ControlCode } from "./ControlCode";

export class FrameHeader {
    static readonly HeaderLength = 1/*control code*/ + 4/*channel id*/ + 2/*payload length*/;

    /**
     * Gets or sets the kind of frame this is.
     */
    code: ControlCode;

    /**
     * Gets or sets the channel that this frame refers to or carries a payload for.
     */
    channelId: number;

    /**
     * Gets or sets the length of the frame content (excluding the header).
     * Must be no greater than 65535 (uint16 max value).
     */
    framePayloadLength: number;

    static Deserialize(buffer: Buffer): FrameHeader {
        if (!buffer || buffer.length != FrameHeader.HeaderLength) {
            throw new Error("buffer must have length of " + FrameHeader.HeaderLength);
        }

        var header = new FrameHeader();
        header.code = buffer[0];
        header.channelId = buffer.readUInt32BE(1);
        header.framePayloadLength = buffer.readUInt16BE(5);
        return header;
    }

    serialize(buffer: Buffer) {
        if (!buffer || buffer.length != FrameHeader.HeaderLength) {
            throw new Error("buffer must have length of " + FrameHeader.HeaderLength);
        }

        buffer[0] = this.code;
        buffer.writeUInt32BE(this.channelId, 1);
        buffer.writeUInt16BE(this.framePayloadLength, 5);
    }
}
