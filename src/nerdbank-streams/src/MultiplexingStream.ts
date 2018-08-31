/* TODO:
 * Anonymous channels (create, accept, reject)
 * Events
 * Cancellation
 * Tracing
 * Fault handling (and reporting via completion promise)
 * Auto-terminate channels when both ends have finished writing (AutoCloseOnPipesClosureAsync)
 * Add .NET interop tests
 */

import { randomBytes } from "crypto";
import { CancellationToken, CancellationTokenSource } from "vscode-jsonrpc";
import { Channel, ChannelClass } from "./Channel";
import { ChannelOptions } from "./ChannelOptions";
import { ControlCode } from "./ControlCode";
import { Deferred } from "./Deferred";
import { FrameHeader } from "./FrameHeader";
import { IDisposableObservable } from "./IDisposableObservable";
import "./MultiplexingStreamOptions";
import { MultiplexingStreamOptions } from "./MultiplexingStreamOptions";
import { getBufferFrom, throwIfDisposed } from "./Utilities";

export abstract class MultiplexingStream implements IDisposableObservable {

    protected get disposalToken() {
        return this.disposalTokenSource.token;
    }

    /**
     * Gets a promise that is resolved or rejected based on how this stream is disposed or fails.
     */
    get completion(): Promise<void> {
        return this._completionSource.promise;
    }

    /**
     * Gets a value indicating whether this instance has been disposed.
     */
    get isDisposed(): boolean {
        return this.disposalTokenSource.token.isCancellationRequested;
    }

    /**
     * Initializes a new instance of the `MultiplexingStream` class.
     * @param stream The duplex stream to read and write to.
     * Use `FullDuplexStream.Splice` if you have distinct input/output streams.
     * @param options Options to customize the behavior of the stream.
     * @param cancellationToken A token whose cancellation aborts the handshake with the remote end.
     * @returns The multiplexing stream, once the handshake is complete.
     */
    public static async CreateAsync(
        stream: NodeJS.ReadWriteStream,
        options?: MultiplexingStreamOptions,
        cancellationToken?: CancellationToken): Promise<MultiplexingStream> {

        if (!stream) {
            throw new Error("stream must be specified.");
        }

        options = options || new MultiplexingStreamOptions();

        // Send the protocol magic number, and a random 16-byte number to establish even/odd assignments.
        const randomSendBuffer = randomBytes(16);
        const sendBuffer = Buffer.concat([MultiplexingStream.protocolMagicNumber, randomSendBuffer]);
        stream.write(sendBuffer);

        const recvBuffer = await getBufferFrom(stream, sendBuffer.length);

        for (let i = 0; i < MultiplexingStream.protocolMagicNumber.length; i++) {
            const expected = MultiplexingStream.protocolMagicNumber[i];
            const actual = recvBuffer.readUInt8(i);
            if (expected !== actual) {
                throw new Error("Protocol magic number mismatch.");
            }
        }

        let isOdd: boolean;
        for (let i = 0; i < randomSendBuffer.length; i++) {
            const sent = randomSendBuffer[i];
            const recv = recvBuffer.readUInt8(MultiplexingStream.protocolMagicNumber.length + i);
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

        return new MultiplexingStreamClass(stream, isOdd, options);
    }

    /**
     * The options to use for channels we create in response to incoming offers.
     * @description Whatever these settings are, they can be replaced when the channel is accepted.
     */
    protected static readonly defaultChannelOptions = new ChannelOptions();

    /**
     * The encoding used for characters in control frames.
     */
    protected static readonly ControlFrameEncoding = "utf-8";
    /**
     * The maximum length of a frame's payload.
     */
    private static readonly framePayloadMaxLength = 20 * 1024;

    /**
     * The magic number to send at the start of communication.
     * If the protocol ever changes, change this random number. It serves both as a way to recognize the other end
     * actually supports multiplexing and ensure compatibility.
     */
    private static readonly protocolMagicNumber = new Buffer([0x2f, 0xdf, 0x1d, 0x50]);
    protected readonly _completionSource = new Deferred<void>();

    /**
     * A dictionary of all channels, keyed by their ID.
     */
    protected readonly openChannels: { [id: number]: ChannelClass } = {};

    /**
     * The last number assigned to a channel.
     * Each use of this should increment by two.
     */
    protected lastOfferedChannelId: number;

    /**
     * A map of channel names to queues of channels waiting for local acceptance.
     */
    protected readonly channelsOfferedByThemByName: { [name: string]: ChannelClass[] } = {};

    /**
     * A map of channel names to queues of Deferred<Channel> from waiting accepters.
     */
    protected readonly acceptingChannels: { [name: string]: Array<Deferred<ChannelClass>> } = {};

    private disposalTokenSource = new CancellationTokenSource();

    protected constructor(protected stream: NodeJS.ReadWriteStream) {
    }

    /**
     * Creates an anonymous channel that may be accepted by <see cref="AcceptChannel(int, ChannelOptions)"/>.
     * Its existance must be communicated by other means (typically another, existing channel) to encourage acceptance.
     * @param options A set of options that describe local treatment of this channel.
     * @returns The anonymous channel.
     * @description Note that while the channel is created immediately, any local write to that channel will be
     * buffered locally until the remote party accepts the channel.
     */
    public createChannel(options?: ChannelOptions): Channel {
        throw new Error("Not yet implemented.");
    }

    /**
     * Accepts a channel with a specific ID.
     * @param id The id of the channel to accept.
     * @param options A set of options that describe local treatment of this channel.
     * @description This method can be used to accept anonymous channels created with <see cref="CreateChannel"/>.
     * Unlike <see cref="AcceptChannelAsync(string, ChannelOptions, CancellationToken)"/> which will await
     * for a channel offer if a matching one has not been made yet, this method only accepts an offer
     * for a channel that has already been made.
     */
    public acceptChannel(id: number, options?: ChannelOptions): Channel {
        throw new Error("Not yet implemented.");
    }

    /**
     * Rejects an offer for the channel with a specified ID.
     * @param id The ID of the channel whose offer should be rejected.
     */
    public rejectChannel(id: number) {
        throw new Error("Not yet implemented.");
    }

    /**
     * Offers a new, named channel to the remote party so they may accept it with
     * [acceptChannelAsync](#acceptChannelAsync).
     * @param name A name for the channel, which must be accepted on the remote end to complete creation.
     * It need not be unique, and may be empty but must not be null.
     * Any characters are allowed, and max length is determined by the maximum frame payload (based on UTF-8 encoding).
     * @param options A set of options that describe local treatment of this channel.
     * @param cancellationToken A cancellation token.
     * @returns A task that completes with the `Channel` if the offer is accepted on the remote end
     * or faults with `MultiplexingProtocolException` if the remote end rejects the channel.
     */
    public async offerChannelAsync(
        name: string,
        options?: ChannelOptions,
        cancellationToken?: CancellationToken): Promise<Channel> {

        if (!name) {
            throw new Error("Name must be specified.");
        }

        throwIfDisposed(this);

        const payload = new Buffer(name, MultiplexingStream.ControlFrameEncoding);
        if (payload.length > MultiplexingStream.framePayloadMaxLength) {
            throw new Error("Name is too long.");
        }

        const channel = new ChannelClass(
            this as any as MultiplexingStreamClass,
            this.getUnusedChannelId(),
            name,
            false, // offeredByThem
            options);
        this.openChannels[channel.id] = channel;

        const header = new FrameHeader();
        header.code = ControlCode.Offer;
        header.framePayloadLength = payload.length;
        header.channelId = channel.id;

        // TODO: add cancellation handling
        await this.sendFrameAsync(header, payload, cancellationToken);
        await channel.acceptance;

        return channel;
    }

    /**
     * Accepts a channel that the remote end has attempted or may attempt to create.
     * @param name The name of the channel to accept.
     * @param options A set of options that describe local treatment of this channel.
     * @param cancellationToken A token to indicate lost interest in accepting the channel.
     * @returns The `Channel`, after its offer has been received from the remote party and accepted.
     * @description If multiple offers exist with the specified `name`, the first one received will be accepted.
     */
    public async acceptChannelAsync(
        name: string,
        options?: ChannelOptions,
        cancellationToken?: CancellationToken): Promise<Channel> {
        if (!name) {
            throw new Error("Name must be specified.");
        }

        throwIfDisposed(this);

        let channel: ChannelClass = null;
        let pendingAcceptChannel: Deferred<ChannelClass>;
        const channelsOfferedByThem = this.channelsOfferedByThemByName[name] as ChannelClass[];
        if (channelsOfferedByThem) {
            while (channel === null && channelsOfferedByThem.length > 0) {
                channel = channelsOfferedByThem.shift();
                if (channel.acceptanceIsCompleted) {
                    channel = null;
                    continue;
                }
            }
        }

        if (channel === null) {
            let acceptingChannels = this.acceptingChannels[name];
            if (!acceptingChannels) {
                this.acceptingChannels[name] = acceptingChannels = [];
            }

            pendingAcceptChannel = new Deferred<ChannelClass>(options);
            acceptingChannels.push(pendingAcceptChannel);
        }

        if (channel != null) {
            this.acceptChannelOrThrow(channel, options);
            return channel;
        } else {
            // TODO: add cancellation handling
            return await pendingAcceptChannel.promise;
        }
    }

    /**
     * Disposes the stream.
     */
    public dispose() {
        this.disposalTokenSource.cancel();
        this._completionSource.resolve();
    }

    protected abstract sendFrameAsync(
        header: FrameHeader,
        payload?: Buffer,
        cancellationToken?: CancellationToken): Promise<void>;

    protected abstract sendFrame(code: ControlCode, channelId: number): Promise<void>;

    protected acceptChannelOrThrow(channel: ChannelClass, options: ChannelOptions) {
        if (channel.tryAcceptOffer(options)) {
            this.sendFrame(ControlCode.OfferAccepted, channel.id);
        } else if (channel.isAccepted) {
            throw new Error("Channel is already accepted.");
        } else if (channel.isRejectedOrCanceled) {
            throw new Error("Channel is no longer available for acceptance.");
        } else {
            throw new Error("Channel could not be accepted.");
        }
    }

    /**
     * Gets a unique number that can be used to represent a channel.
     * @description The channel numbers increase by two in order to maintain odd or even numbers,
     * since each party is allowed to create only one or the other.
     */
    private getUnusedChannelId() {
        return this.lastOfferedChannelId += 2;
    }
}

// tslint:disable-next-line:max-classes-per-file
export class MultiplexingStreamClass extends MultiplexingStream {
    constructor(stream: NodeJS.ReadWriteStream, private isOdd: boolean, private options: MultiplexingStreamOptions) {
        super(stream);

        this.lastOfferedChannelId = isOdd ? -1 : 0; // the first channel created should be 1 or 2

        // Initiate reading from the transport stream. This will not end until the stream does, or we're disposed.
        // If reading the stream fails, we'll dispose ourselves.
        this.readFromStream(this.disposalToken);
    }

    public sendFrameAsync(header: FrameHeader, payload?: Buffer, cancellationToken?: CancellationToken) {
        if (!header) {
            throw new Error("Header is required.");
        }

        throwIfDisposed(this);

        const headerBuffer = new Buffer(FrameHeader.HeaderLength);
        header.serialize(headerBuffer);

        const frame = payload && payload.length > 0 ? Buffer.concat([headerBuffer, payload]) : headerBuffer;
        const deferred = new Deferred<void>();
        this.stream.write(frame, deferred.resolve.bind(deferred));
        return deferred.promise;
    }

    public async sendFrame(code: ControlCode, channelId: number) {
        if (this._completionSource.isCompleted) {
            // Any frames that come in after we're done are most likely frames just informing that channels are
            // being terminated, which we do not need to communicate since the connection going down implies that.
            return;
        }

        const header = new FrameHeader();
        header.code = code;
        header.channelId = channelId;
        header.framePayloadLength = 0;
        await this.sendFrameAsync(header);
    }

    public async onChannelWritingCompleted(channel: ChannelClass) {
        // Only inform the remote side if this channel has not already been terminated.
        if (!channel.isDisposed && this.openChannels[channel.id]) {
            await this.sendFrame(ControlCode.ContentWritingCompleted, channel.id);
        }
    }

    public async onChannelDisposed(channel: ChannelClass) {
        if (!this._completionSource.isCompleted) {
            await this.sendFrame(ControlCode.ChannelTerminated, channel.id);
        }
    }

    private async readFromStream(cancellationToken: CancellationToken) {
        while (!this.isDisposed) {
            const headerBuffer = await getBufferFrom(this.stream, FrameHeader.HeaderLength, true, cancellationToken);
            if (headerBuffer.length === 0) {
                break;
            }

            const header = FrameHeader.Deserialize(headerBuffer);
            switch (header.code) {
                case ControlCode.Offer:
                    await this.onOffer(header.channelId, header.framePayloadLength, cancellationToken);
                    break;
                case ControlCode.OfferAccepted:
                    this.onOfferAccepted(header.channelId);
                    break;
                case ControlCode.Content:
                    await this.onContent(header, cancellationToken);
                    break;
                case ControlCode.ContentWritingCompleted:
                    this.onContentWritingCompleted(header.channelId);
                    break;
                case ControlCode.ChannelTerminated:
                    this.onChannelTerminated(header.channelId);
                    break;
                default:
                    break;
            }
        }

        this.dispose();
    }

    private async onOffer(channelId: number, payloadSize: number, cancellationToken: CancellationToken) {
        const payload = await getBufferFrom(this.stream, payloadSize, null, cancellationToken);
        const name = payload.toString(MultiplexingStream.ControlFrameEncoding);

        const channel = new ChannelClass(this, channelId, name, true, MultiplexingStream.defaultChannelOptions);
        let acceptingChannelAlreadyPresent = false;
        let options: ChannelOptions = null;

        let acceptingChannels: Array<Deferred<Channel>>;
        if (name != null && (acceptingChannels = this.acceptingChannels[name]) != null) {
            while (acceptingChannels.length > 0) {
                const candidate = acceptingChannels.shift();
                if (candidate.resolve(channel)) {
                    acceptingChannelAlreadyPresent = true;
                    options = candidate.state as ChannelOptions;
                    break;
                }
            }
        }

        if (!acceptingChannelAlreadyPresent) {
            if (name != null) {
                let offeredChannels: Channel[];
                if (!(offeredChannels = this.channelsOfferedByThemByName[name])) {
                    this.channelsOfferedByThemByName[name] = offeredChannels = [];
                }

                offeredChannels.push(channel);
            }
        }

        this.openChannels[channelId] = channel;

        if (acceptingChannelAlreadyPresent) {
            this.acceptChannelOrThrow(channel, options);
        }
    }

    private onOfferAccepted(channelId: number) {
        const channel = this.openChannels[channelId] as ChannelClass;
        if (!channel) {
            throw new Error("Unexpected channel created with ID " + channelId);
        }

        if (!channel.onAccepted()) {
            // This may be an acceptance of a channel that we canceled an offer for, and a race condition
            // led to our cancellation notification crossing in transit with their acceptance notification.
            // In this case, do nothing since we already sent a channel termination message, and the remote side
            // should notice it soon.
        }
    }

    private async onContent(header: FrameHeader, cancellationToken: CancellationToken) {
        const channel = this.openChannels[header.channelId] as ChannelClass;

        const buffer = await getBufferFrom(this.stream, header.framePayloadLength, false, cancellationToken);
        channel.onContent(buffer);
    }

    private onContentWritingCompleted(channelId: number) {
        const channel = this.openChannels[channelId] as ChannelClass;
        channel.onContent(null); // signify that the remote is done writing.
    }

    /**
     * Occurs when the remote party has terminated a channel (including canceling an offer).
     * @param channelId The ID of the terminated channel.
     */
    private onChannelTerminated(channelId: number) {
        const channel = this.openChannels[channelId];
        if (channel) {
            delete this.openChannels[channelId];
            if (channel.name) {
                const queue: Channel[] = this.channelsOfferedByThemByName[channel.name];
                if (queue) {
                    const idx = queue.indexOf(channel);
                    if (idx >= 0) {
                        queue.splice(idx, 1);
                    }
                }
            }
            channel.dispose();
        }
    }
}
