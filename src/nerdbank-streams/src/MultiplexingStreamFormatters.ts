import { OfferParameters } from "./OfferParameters";
import { AcceptanceParameters } from "./AcceptanceParameters";
import { MultiplexingStream } from "./MultiplexingStream";
import { randomBytes } from "crypto";
import { getBufferFrom, writeAsync } from "./Utilities";
import CancellationToken from "cancellationtoken";
import * as msgpack from 'msgpack-lite';
import { Deferred } from "./Deferred";
import { FrameHeader } from "./FrameHeader";
import { ControlCode } from "./ControlCode";
import { ChannelSource } from "./QualifiedChannelId";

export interface Version {
    major: number;
    minor: number;
}

export interface HandshakeResult {
    isOdd?: boolean;
    protocolVersion: Version;
}

export abstract class MultiplexingStreamFormatter {
    isOdd?: boolean;

    abstract writeHandshakeAsync(): Promise<Buffer | null>;
    abstract readHandshakeAsync(writeHandshakeResult: Buffer | null, cancellationToken: CancellationToken): Promise<HandshakeResult>;

    abstract writeFrameAsync(header: FrameHeader, payload?: Buffer): Promise<void>;
    abstract readFrameAsync(cancellationToken: CancellationToken): Promise<{ header: FrameHeader, payload: Buffer } | null>;

    abstract serializeOfferParameters(offer: OfferParameters): Buffer;
    abstract deserializeOfferParameters(payload: Buffer): OfferParameters;

    abstract serializeAcceptanceParameters(acceptance: AcceptanceParameters): Buffer;
    abstract deserializeAcceptanceParameters(payload: Buffer): AcceptanceParameters;

    abstract serializeContentProcessed(bytesProcessed: number): Buffer;
    abstract deserializeContentProcessed(payload: Buffer): number;

    abstract end(): void;

    protected static getIsOddRandomData(): Buffer {
        return randomBytes(16);
    }

    protected static isOdd(localRandom: Buffer, remoteRandom: Buffer): boolean {
        let isOdd: boolean | undefined;
        for (let i = 0; i < localRandom.length; i++) {
            const sent = localRandom[i];
            const recv = remoteRandom[i];
            if (sent > recv) {
                isOdd = true;
                break;
            } else if (sent < recv) {
                isOdd = false;
                break;
            }
        }

        if (isOdd === undefined) {
            throw new Error("Unable to determine even/odd party.");
        }

        return isOdd;
    }

    protected createFrameHeader(code: ControlCode, id: number | undefined): FrameHeader {
        if (!id) {
            return new FrameHeader(code);
        }

        const channelIsOdd = id % 2 === 1;

        // Remember that this is from the remote sender's point of view.
        const source = channelIsOdd === this.isOdd ? ChannelSource.Remote : ChannelSource.Local;
        return new FrameHeader(code, { id, source });
    }
}

// tslint:disable-next-line: max-classes-per-file
export class MultiplexingStreamV1Formatter extends MultiplexingStreamFormatter {
    /**
     * The magic number to send at the start of communication when using v1 of the protocol.
     */
    private static readonly protocolMagicNumber = Buffer.from([0x2f, 0xdf, 0x1d, 0x50]);

    constructor(private readonly stream: NodeJS.ReadWriteStream) {
        super();
    }

    end() {
        this.stream.end();
    }

    async writeHandshakeAsync(): Promise<Buffer> {
        const randomSendBuffer = MultiplexingStreamFormatter.getIsOddRandomData();
        const sendBuffer = Buffer.concat([MultiplexingStreamV1Formatter.protocolMagicNumber, randomSendBuffer]);
        await writeAsync(this.stream, sendBuffer);
        return randomSendBuffer;
    }

    async readHandshakeAsync(writeHandshakeResult: Buffer, cancellationToken: CancellationToken): Promise<HandshakeResult> {
        const localRandomBuffer = writeHandshakeResult as Buffer;
        const recvBuffer = await getBufferFrom(this.stream, MultiplexingStreamV1Formatter.protocolMagicNumber.length + 16, false, cancellationToken);

        for (let i = 0; i < MultiplexingStreamV1Formatter.protocolMagicNumber.length; i++) {
            const expected = MultiplexingStreamV1Formatter.protocolMagicNumber[i];
            const actual = recvBuffer.readUInt8(i);
            if (expected !== actual) {
                throw new Error(`Protocol magic number mismatch. Expected ${expected} but was ${actual}.`);
            }
        }

        const isOdd = MultiplexingStreamFormatter.isOdd(localRandomBuffer, recvBuffer.slice(MultiplexingStreamV1Formatter.protocolMagicNumber.length));

        return { isOdd, protocolVersion: { major: 1, minor: 0 } };
    }

    async writeFrameAsync(header: FrameHeader, payload?: Buffer): Promise<void> {
        const headerBuffer = Buffer.alloc(7);
        headerBuffer.writeInt8(header.code, 0);
        headerBuffer.writeUInt32BE(header.channel?.id || 0, 1);
        headerBuffer.writeUInt16BE(payload?.length || 0, 5);
        await writeAsync(this.stream, headerBuffer);
        if (payload && payload.length > 0) {
            await writeAsync(this.stream, payload);
        }
    }

    async readFrameAsync(cancellationToken: CancellationToken): Promise<{ header: FrameHeader, payload: Buffer } | null> {
        if (this.isOdd === undefined) {
            throw new Error("isOdd must be set first.");
        }

        const headerBuffer = await getBufferFrom(this.stream, 7, true, cancellationToken);
        if (headerBuffer === null) {
            return null;
        }

        const header = this.createFrameHeader(
            headerBuffer.readInt8(0),
            headerBuffer.readUInt32BE(1));
        const payloadLength = headerBuffer.readUInt16BE(5);
        const payload = await getBufferFrom(this.stream, payloadLength);
        return { header, payload };
    }

    serializeOfferParameters(offer: OfferParameters): Buffer {
        const payload = Buffer.from(offer.name, MultiplexingStream.ControlFrameEncoding);
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

    serializeAcceptanceParameters(_: AcceptanceParameters): Buffer {
        return Buffer.from([]);
    }

    deserializeAcceptanceParameters(_: Buffer): AcceptanceParameters {
        return {};
    }

    serializeContentProcessed(bytesProcessed: number): Buffer {
        throw new Error("Not supported in the V1 protocol.");
    }

    deserializeContentProcessed(payload: Buffer): number {
        throw new Error("Not supported in the V1 protocol.");
    }
}

// tslint:disable-next-line: max-classes-per-file
export class MultiplexingStreamV2Formatter extends MultiplexingStreamFormatter {
    private static readonly ProtocolVersion: Version = { major: 2, minor: 0 };
    private readonly reader: msgpack.DecodeStream;
    protected readonly writer: NodeJS.WritableStream;

    constructor(stream: NodeJS.ReadWriteStream) {
        super();
        this.reader = msgpack.createDecodeStream();
        stream.pipe(this.reader);

        this.writer = stream;
    }

    end() {
        this.writer.end();
    }

    async writeHandshakeAsync(): Promise<Buffer | null> {
        const randomData = MultiplexingStreamFormatter.getIsOddRandomData();
        const msgpackObject = [[MultiplexingStreamV2Formatter.ProtocolVersion.major, MultiplexingStreamV2Formatter.ProtocolVersion.minor], randomData];
        await writeAsync(this.writer, msgpack.encode(msgpackObject));
        return randomData;
    }

    async readHandshakeAsync(writeHandshakeResult: Buffer | null, cancellationToken: CancellationToken): Promise<HandshakeResult> {
        if (!writeHandshakeResult) {
            throw new Error("Provide the result of writeHandshakeAsync as a first argument.");
        }

        const handshake = await this.readMessagePackAsync(cancellationToken);
        if (handshake === null) {
            throw new Error("No data received during handshake.");
        }

        return {
            isOdd: MultiplexingStreamFormatter.isOdd(writeHandshakeResult, handshake[1]),
            protocolVersion: { major: handshake[0][0], minor: handshake[0][1] },
        };
    }

    async writeFrameAsync(header: FrameHeader, payload?: Buffer): Promise<void> {
        const msgpackObject: any[] = [header.code];
        if (header.channel?.id) {
            msgpackObject.push(header.channel.id);
            if (payload && payload.length > 0) {
                msgpackObject.push(payload);
            }
        } else if (payload && payload.length > 0) {
            throw new Error("A frame may not contain payload without a channel ID.");
        }

        await writeAsync(this.writer, msgpack.encode(msgpackObject));
    }

    async readFrameAsync(cancellationToken: CancellationToken): Promise<{ header: FrameHeader; payload: Buffer; } | null> {
        if (this.isOdd === undefined) {
            throw new Error("isOdd must be set first.");
        }

        const msgpackObject = await this.readMessagePackAsync(cancellationToken) as [ControlCode, number, Buffer] | null;
        if (msgpackObject === null) {
            return null;
        }

        const header = this.createFrameHeader(
            msgpackObject[0],
            msgpackObject[1]);
        return {
            header,
            payload: msgpackObject[2] || Buffer.from([]),
        }
    }

    serializeOfferParameters(offer: OfferParameters): Buffer {
        const payload: any[] = [offer.name];
        if (offer.remoteWindowSize) {
            payload.push(offer.remoteWindowSize);
        }

        return msgpack.encode(payload);
    }

    deserializeOfferParameters(payload: Buffer): OfferParameters {
        const msgpackObject = msgpack.decode(payload);
        return {
            name: msgpackObject[0],
            remoteWindowSize: msgpackObject[1],
        };
    }

    serializeAcceptanceParameters(acceptance: AcceptanceParameters): Buffer {
        const payload: any[] = [];
        if (acceptance.remoteWindowSize) {
            payload.push(acceptance.remoteWindowSize);
        }

        return msgpack.encode(payload);
    }

    deserializeAcceptanceParameters(payload: Buffer): AcceptanceParameters {
        const msgpackObject = msgpack.decode(payload);
        return {
            remoteWindowSize: msgpackObject[0],
        };
    }

    serializeContentProcessed(bytesProcessed: number): Buffer {
        return msgpack.encode([bytesProcessed]);
    }

    deserializeContentProcessed(payload: Buffer): number {
        return msgpack.decode(payload)[0];
    }

    serializeException(error: Error | null): Buffer {
        // If the error doesn't exist then return an empty buffer.
        if (!error) {
            return Buffer.alloc(0);
        }

        const errorMsg: string = `${error.name}: ${error.message}`;
        const payload: any[] = [errorMsg];
        return msgpack.encode(payload);
    }

    deserializeException(payload: Buffer): Error | null {
        // If the payload is empty then return null.
        if (payload.length === 0) {
            return null;
        }

        // Make sure that the message pack object contains a message
        const msgpackObject = msgpack.decode(payload);
        if (!msgpackObject || msgpackObject.length === 0) {
            return null;
        }

        // Get error message and return the error to the remote side
        let errorMsg: string = msgpack.decode(payload)[0];
        errorMsg = `Received error from remote side: ${errorMsg}`;
        return new Error(errorMsg);
    }

    protected async readMessagePackAsync(cancellationToken: CancellationToken): Promise<{} | [] | null> {
        const streamEnded = new Deferred<void>();
        while (true) {
            const readObject = this.reader.read();
            if (readObject === null) {
                const bytesAvailable = new Deferred<void>();
                const bytesAvailableCallback = bytesAvailable.resolve.bind(bytesAvailable);
                const streamEndedCallback = streamEnded.resolve.bind(streamEnded);

                this.reader.once("readable", bytesAvailableCallback);
                this.reader.once("end", streamEndedCallback);
                const endPromise = Promise.race([bytesAvailable.promise, streamEnded.promise]);
                await (cancellationToken ? cancellationToken.racePromise(endPromise) : endPromise);

                this.reader.removeListener("readable", bytesAvailableCallback);
                this.reader.removeListener("end", streamEndedCallback);

                if (bytesAvailable.isCompleted) {
                    continue;
                }

                return null;
            }

            return readObject;
        }
    }
}

// tslint:disable-next-line: max-classes-per-file
export class MultiplexingStreamV3Formatter extends MultiplexingStreamV2Formatter {
    private static readonly ProtocolV3Version: Version = { major: 2, minor: 0 };

    writeHandshakeAsync(): Promise<null> {
        return Promise.resolve(null);
    }

    readHandshakeAsync(): Promise<HandshakeResult> {
        return Promise.resolve({ protocolVersion: MultiplexingStreamV3Formatter.ProtocolV3Version });
    }

    async writeFrameAsync(header: FrameHeader, payload?: Buffer): Promise<void> {
        const msgpackObject: any[] = [header.code];
        if (header.channel) {
            msgpackObject.push(header.channel.id);
            msgpackObject.push(header.channel.source);
            if (payload && payload.length > 0) {
                msgpackObject.push(payload);
            }
        } else if (payload && payload.length > 0) {
            throw new Error("A frame may not contain payload without a channel ID.");
        }

        await writeAsync(this.writer, msgpack.encode(msgpackObject));
    }

    async readFrameAsync(cancellationToken: CancellationToken): Promise<{ header: FrameHeader; payload: Buffer; } | null> {
        const msgpackObject = await this.readMessagePackAsync(cancellationToken) as [ControlCode, number, ChannelSource, Buffer] | null;
        if (msgpackObject === null) {
            return null;
        }

        const header = new FrameHeader(
            msgpackObject[0],
            msgpackObject.length > 1 ? { id: msgpackObject[1], source: msgpackObject[2] } : undefined);
        return {
            header,
            payload: msgpackObject[3] || Buffer.from([]),
        }
    }
}
