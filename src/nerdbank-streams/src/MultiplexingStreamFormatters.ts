import { OfferParameters } from "./OfferParameters";
import { AcceptanceParameters } from "./AcceptanceParameters";
import { MultiplexingStream } from "./MultiplexingStream";

export abstract class MultiplexingStreamFormatter {
    abstract serializeOfferParameters(offer: OfferParameters): Buffer;
    abstract deserializeOfferParameters(buffer: Buffer): OfferParameters;

    abstract serializerAcceptanceParameters(acceptance: AcceptanceParameters): Buffer;
    abstract deserializerAcceptanceParameters(buffer: Buffer): AcceptanceParameters;
}

export class MultiplexingStreamV1Formatter implements MultiplexingStreamFormatter {
    serializeOfferParameters(offer: OfferParameters): Buffer {
        const payload = new Buffer(offer.name, MultiplexingStream.ControlFrameEncoding);
        if (payload.length > MultiplexingStream.framePayloadMaxLength) {
            throw new Error("Name is too long.");
        }

        return payload;
    }

    deserializeOfferParameters(payload: Buffer): OfferParameters {
        return {
            name: payload.toString(MultiplexingStream.ControlFrameEncoding),
        };
    }

    serializerAcceptanceParameters(_: AcceptanceParameters): Buffer {
        return new Buffer([]);
    }

    deserializerAcceptanceParameters(_: Buffer): AcceptanceParameters {
        return {};
    }
}

export class MultiplexingStreamV2Formatter implements MultiplexingStreamFormatter {
    serializeOfferParameters(offer: OfferParameters): Buffer {
        const nameBytes = new Buffer(offer.name, MultiplexingStream.ControlFrameEncoding);

        const windowSizeBytes = new Buffer(4);
        if (offer.remoteWindowSize === undefined) {
            throw new Error("remoteWindowSize must be set.");
        }
        windowSizeBytes.writeInt32BE(offer.remoteWindowSize, 0);

        const payload = Buffer.concat([nameBytes, windowSizeBytes]);
        if (payload.length > MultiplexingStream.framePayloadMaxLength) {
            throw new Error("Name is too long.");
        }

        return payload;
    }

    deserializeOfferParameters(payload: Buffer): OfferParameters {
        return {
            name: payload.toString(MultiplexingStream.ControlFrameEncoding, 0, payload.length - 4),
            remoteWindowSize: payload.readInt32BE(payload.length - 4),
        };
    }

    serializerAcceptanceParameters(acceptance: AcceptanceParameters): Buffer {
        const payload = new Buffer(4);
        if (acceptance.remoteWindowSize === undefined) {
            throw new Error("remoteWindowSize must be set.");
        }
        payload.writeInt32BE(acceptance.remoteWindowSize, 0);
        return payload;
    }

    deserializerAcceptanceParameters(buffer: Buffer): AcceptanceParameters {
        return {
            remoteWindowSize: buffer.readInt32BE(0),
        };
    }
}
