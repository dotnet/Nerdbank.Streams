/* TODO:
 * Tracing
 * Auto-terminate channels when both ends have finished writing (AutoCloseOnPipesClosureAsync)
 */

import CancellationToken from "cancellationtoken";
import caught = require("caught");
import { EventEmitter } from "events";
import { Channel, ChannelClass } from "./Channel";
import { ChannelOptions } from "./ChannelOptions";
import { ControlCode } from "./ControlCode";
import { Deferred } from "./Deferred";
import { FrameHeader } from "./FrameHeader";
import { IChannelOfferEventArgs } from "./IChannelOfferEventArgs";
import { IDisposableObservable } from "./IDisposableObservable";
import "./MultiplexingStreamOptions";
import { MultiplexingStreamOptions } from "./MultiplexingStreamOptions";
import { removeFromQueue, throwIfDisposed } from "./Utilities";
import {
    MultiplexingStreamFormatter,
    MultiplexingStreamV1Formatter,
    MultiplexingStreamV2Formatter,
    MultiplexingStreamV3Formatter,
} from "./MultiplexingStreamFormatters";
import { OfferParameters } from "./OfferParameters";
import { Semaphore } from 'await-semaphore';
import { QualifiedChannelId, ChannelSource } from "./QualifiedChannelId";

export abstract class MultiplexingStream implements IDisposableObservable {
    /**
     * The maximum length of a frame's payload.
     */
    static readonly framePayloadMaxLength = 20 * 1024;

    private static readonly recommendedDefaultChannelReceivingWindowSize = 5 * MultiplexingStream.framePayloadMaxLength;

    /** The default window size used for new channels that do not specify a value for ChannelOptions.ChannelReceivingWindowSize. */
    readonly defaultChannelReceivingWindowSize: number;

    protected readonly formatter: MultiplexingStreamFormatter;

    protected get disposalToken() {
        return this.disposalTokenSource.token;
    }

    /**
     * Gets a promise that is resolved or rejected based on how this stream is disposed or fails.
     */
    public get completion(): Promise<void> {
        return this._completionSource.promise;
    }

    /**
     * Gets a value indicating whether this instance has been disposed.
     */
    public get isDisposed(): boolean {
        return this.disposalTokenSource.token.isCancelled;
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
        cancellationToken: CancellationToken = CancellationToken.CONTINUE): Promise<MultiplexingStream> {

        if (!stream) {
            throw new Error("stream must be specified.");
        }

        options = options || {};
        options.protocolMajorVersion ??= 1;
        options.defaultChannelReceivingWindowSize = options.defaultChannelReceivingWindowSize || MultiplexingStream.recommendedDefaultChannelReceivingWindowSize;
        if (options.protocolMajorVersion < 3 && options.seededChannels && options.seededChannels.length > 0) {
            throw new Error("Seeded channels require v3 of the protocol at least.");
        }

        // Send the protocol magic number, and a random 16-byte number to establish even/odd assignments.
        const formatter: MultiplexingStreamFormatter | undefined =
            options.protocolMajorVersion === 1 ? new MultiplexingStreamV1Formatter(stream) :
            options.protocolMajorVersion === 2 ? new MultiplexingStreamV2Formatter(stream) :
            options.protocolMajorVersion === 3 ? new MultiplexingStreamV3Formatter(stream) :
            undefined;
        if (!formatter) {
            throw new Error(`Protocol major version ${options.protocolMajorVersion} is not supported.`);
        }

        const writeHandshakeData = await formatter.writeHandshakeAsync();
        const handshakeResult = await formatter.readHandshakeAsync(writeHandshakeData, cancellationToken);
        formatter.isOdd = handshakeResult.isOdd;

        return new MultiplexingStreamClass(stream, handshakeResult.isOdd, options);
    }

    /**
     * Initializes a new instance of the `MultiplexingStream` class
     * with protocolMajorVersion set to 3.
     * @param stream The duplex stream to read and write to.
     * Use `FullDuplexStream.Splice` if you have distinct input/output streams.
     * @param options Options to customize the behavior of the stream.
     * @returns The multiplexing stream.
     */
     public static Create(
        stream: NodeJS.ReadWriteStream,
        options?: MultiplexingStreamOptions) : MultiplexingStream {
        options ??= { protocolMajorVersion: 3 };
        options.protocolMajorVersion ??= 3;

        const formatter: MultiplexingStreamFormatter | undefined =
            options.protocolMajorVersion === 3 ? new MultiplexingStreamV3Formatter(stream) :
            undefined;
        if (!formatter) {
            throw new Error(`Protocol major version ${options.protocolMajorVersion} is not supported. Try CreateAsync instead.`);
        }

        return new MultiplexingStreamClass(stream, undefined, options);
    }

    /**
     * The options to use for channels we create in response to incoming offers.
     * @description Whatever these settings are, they can be replaced when the channel is accepted.
     */
    protected static readonly defaultChannelOptions: ChannelOptions = {};

    /**
     * The encoding used for characters in control frames.
     */
    static readonly ControlFrameEncoding = "utf-8";

    protected readonly _completionSource = new Deferred<void>();

    /**
     * A dictionary of channels that were seeded, keyed by their ID.
     */
    protected readonly seededOpenChannels: { [id: number]: ChannelClass } = {};

    /**
     * A dictionary of channels that were offered locally, keyed by their ID.
     */
    protected readonly locallyOfferedOpenChannels: { [id: number]: ChannelClass } = {};

    /**
     * A dictionary of channels that were offered remotely, keyed by their ID.
     */
    protected readonly remotelyOfferedOpenChannels: { [id: number]: ChannelClass } = {};

    /**
     * The last number assigned to a channel.
     * Each use of this should increment by two if isOdd is defined.
     * It should never exceed uint32.MaxValue
     */
    protected abstract lastOfferedChannelId: number;

    /**
     * A map of channel names to queues of channels waiting for local acceptance.
     */
    protected readonly channelsOfferedByThemByName: { [name: string]: ChannelClass[] } = {};

    /**
     * A map of channel names to queues of Deferred<Channel> from waiting accepters.
     */
    protected readonly acceptingChannels: { [name: string]: Deferred<ChannelClass>[] } = {};

    /** The major version of the protocol being used for this connection. */
    protected readonly protocolMajorVersion: number;

    private readonly eventEmitter = new EventEmitter();

    private disposalTokenSource = CancellationToken.create();

    protected constructor(stream: NodeJS.ReadWriteStream, private readonly isOdd: boolean | undefined, options: MultiplexingStreamOptions) {
        this.defaultChannelReceivingWindowSize = options.defaultChannelReceivingWindowSize ?? MultiplexingStream.recommendedDefaultChannelReceivingWindowSize;
        this.protocolMajorVersion = options.protocolMajorVersion ?? 1;
        const formatter: MultiplexingStreamFormatter | undefined =
            options.protocolMajorVersion === 1 ? new MultiplexingStreamV1Formatter(stream) :
                options.protocolMajorVersion === 2 ? new MultiplexingStreamV2Formatter(stream) :
                    options.protocolMajorVersion === 3 ? new MultiplexingStreamV3Formatter(stream) :
                        undefined;
        if (formatter === undefined) {
            throw new Error(`Unsupported major protocol version: ${options.protocolMajorVersion}`);
        }
        formatter.isOdd = isOdd;
        this.formatter = formatter;

        if (options.seededChannels) {
            for (let i = 0; i < options.seededChannels.length; i++) {
                const channelOptions = options.seededChannels[i];
                const id = { id: i, source: ChannelSource.Seeded };
                this.setOpenChannel(new ChannelClass(this as unknown as MultiplexingStreamClass, id, { name: '', remoteWindowSize: channelOptions.channelReceivingWindowSize ?? this.defaultChannelReceivingWindowSize }));
            }
        }
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
        const offerParameters: OfferParameters = {
            name: "",
            remoteWindowSize: options?.channelReceivingWindowSize ?? this.defaultChannelReceivingWindowSize
        };
        const payload = this.formatter.serializeOfferParameters(offerParameters);
        const channel = new ChannelClass(
            this as any as MultiplexingStreamClass,
            { id: this.getUnusedChannelId(), source: ChannelSource.Local },
            offerParameters);
        this.setOpenChannel(channel);

        this.rejectOnFailure(this.sendFrameAsync(new FrameHeader(ControlCode.Offer, channel.qualifiedId), payload, this.disposalToken));
        return channel;
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
        const channel = this.remotelyOfferedOpenChannels[id] || this.seededOpenChannels[id];
        if (!channel) {
            throw new Error("No channel with ID " + id);
        }

        this.removeChannelFromOfferedQueue(channel);

        this.acceptChannelOrThrow(channel, options);
        return channel;
    }

    /**
     * Rejects an offer for the channel with a specified ID.
     * @param id The ID of the channel whose offer should be rejected.
     */
    public rejectChannel(id: number) {
        const channel = this.remotelyOfferedOpenChannels[id];
        if (channel) {
            removeFromQueue(channel, this.channelsOfferedByThemByName[channel.name]);
        } else {
            throw new Error("No channel with that ID found.");
        }

        // Rejecting a channel rejects a couple promises that we don't want the caller to have to observe
        // separately since they are explicitly stating they want to take this action now.
        caught(channel.acceptance);
        caught(channel.completion);

        channel.dispose();
    }

    /**
     * Offers a new, named channel to the remote party so they may accept it with
     * {@link acceptChannelAsync}.
     * @param name A name for the channel, which must be accepted on the remote end to complete creation.
     * It need not be unique, and may be empty but must not be null.
     * Any characters are allowed, and max length is determined by the maximum frame payload (based on UTF-8 encoding).
     * @param options A set of options that describe local treatment of this channel.
     * @param cancellationToken A cancellation token. Do NOT let this be a long-lived token
     * or a memory leak will result since we add continuations to its promise.
     * @returns A task that completes with the `Channel` if the offer is accepted on the remote end
     * or faults with `MultiplexingProtocolException` if the remote end rejects the channel.
     */
    public async offerChannelAsync(
        name: string,
        options?: ChannelOptions,
        cancellationToken: CancellationToken = CancellationToken.CONTINUE): Promise<Channel> {

        if (name == null) {
            throw new Error("Name must be specified (but may be empty).");
        }

        cancellationToken.throwIfCancelled();
        throwIfDisposed(this);

        const offerParameters: OfferParameters = {
            name,
            remoteWindowSize: options?.channelReceivingWindowSize ?? this.defaultChannelReceivingWindowSize,
        };
        const payload = this.formatter.serializeOfferParameters(offerParameters);
        const channel = new ChannelClass(
            this as any as MultiplexingStreamClass,
            { id: this.getUnusedChannelId(), source: ChannelSource.Local },
            offerParameters);
        this.setOpenChannel(channel);

        const header = new FrameHeader(ControlCode.Offer, channel.qualifiedId);

        const unsubscribeFromCT = cancellationToken.onCancelled((reason) => this.offerChannelCanceled(channel, reason));
        try {
            // We *will* recognize rejection of this promise. But just in case sendFrameAsync completes synchronously,
            // we want to signify that we *will* catch it first to avoid node.js emitting warnings or crashing.
            caught(channel.acceptance);

            await this.sendFrameAsync(header, payload, cancellationToken);
            await channel.acceptance;

            return channel;
        } finally {
            unsubscribeFromCT();
        }
    }

    /**
     * Accepts a channel that the remote end has attempted or may attempt to create.
     * @param name The name of the channel to accept.
     * @param options A set of options that describe local treatment of this channel.
     * @param cancellationToken A token to indicate lost interest in accepting the channel.
     * Do NOT let this be a long-lived token
     * or a memory leak will result since we add continuations to its promise.
     * @returns The `Channel`, after its offer has been received from the remote party and accepted.
     * @description If multiple offers exist with the specified `name`, the first one received will be accepted.
     */
    public async acceptChannelAsync(
        name: string,
        options?: ChannelOptions,
        cancellationToken: CancellationToken = CancellationToken.CONTINUE): Promise<Channel> {
        if (name == null) {
            throw new Error("Name must be specified (but may be empty).");
        }

        cancellationToken.throwIfCancelled();
        throwIfDisposed(this);

        let channel: ChannelClass | undefined;
        let pendingAcceptChannel: Deferred<ChannelClass>;
        const channelsOfferedByThem = this.channelsOfferedByThemByName[name] as ChannelClass[];
        if (channelsOfferedByThem) {
            while (channel === undefined && channelsOfferedByThem.length > 0) {
                channel = channelsOfferedByThem.shift()!;
                if (channel.isAccepted || channel.isRejectedOrCanceled) {
                    channel = undefined;
                    continue;
                }
            }
        }

        if (channel === undefined) {
            let acceptingChannels = this.acceptingChannels[name];
            if (!acceptingChannels) {
                this.acceptingChannels[name] = acceptingChannels = [];
            }

            pendingAcceptChannel = new Deferred<ChannelClass>(options);
            acceptingChannels.push(pendingAcceptChannel);
        }

        if (channel !== undefined) {
            this.acceptChannelOrThrow(channel, options);
            return channel;
        } else {
            const unsubscribeFromCT = cancellationToken.onCancelled(
                (reason) => this.acceptChannelCanceled(pendingAcceptChannel, name, reason));
            try {
                return await pendingAcceptChannel!.promise;
            } finally {
                unsubscribeFromCT();
            }
        }
    }

    /**
     * Disposes the stream.
     */
    public dispose() {
        this.disposalTokenSource.cancel();
        this._completionSource.resolve();

        this.formatter.end();
        [this.locallyOfferedOpenChannels, this.remotelyOfferedOpenChannels].forEach(cb => {
            for (const channelId in cb) {
                if (cb.hasOwnProperty(channelId)) {
                    const channel = cb[channelId];

                    // Acceptance gets rejected when a channel is disposed.
                    // Avoid a node.js crash or test failure for unobserved channels (e.g. offers for channels from the other party that no one cared to receive on this side).
                    caught(channel.acceptance);

                    channel.dispose();
                }
            }
        });
    }

    public on(event: "channelOffered", listener: (args: IChannelOfferEventArgs) => void) {
        this.eventEmitter.on(event, listener);
    }

    public off(event: "channelOffered", listener: (args: IChannelOfferEventArgs) => void) {
        this.eventEmitter.off(event, listener);
    }

    public once(event: "channelOffered", listener: (args: IChannelOfferEventArgs) => void) {
        this.eventEmitter.once(event, listener);
    }

    protected raiseChannelOffered(id: number, name: string, isAccepted: boolean) {
        const args: IChannelOfferEventArgs = {
            id,
            isAccepted,
            name,
        };
        try {
            this.eventEmitter.emit("channelOffered", args);
        } catch (err) {
            this._completionSource.reject(err);
        }
    }

    protected abstract sendFrameAsync(
        header: FrameHeader,
        payload: Buffer,
        cancellationToken: CancellationToken): Promise<void>;

    protected abstract sendFrame(code: ControlCode, channelId: QualifiedChannelId): Promise<void>;

    protected acceptChannelOrThrow(channel: ChannelClass, options?: ChannelOptions) {
        if (channel.tryAcceptOffer(options)) {
            const acceptanceParameters = {
                remoteWindowSize: options?.channelReceivingWindowSize ?? channel.localWindowSize ?? this.defaultChannelReceivingWindowSize,
            };
            if (acceptanceParameters.remoteWindowSize < this.defaultChannelReceivingWindowSize) {
                acceptanceParameters.remoteWindowSize = this.defaultChannelReceivingWindowSize;
            }

            if (channel.qualifiedId.source !== ChannelSource.Seeded) {
                const payload = this.formatter.serializeAcceptanceParameters(acceptanceParameters);
                this.rejectOnFailure(this.sendFrameAsync(new FrameHeader(ControlCode.OfferAccepted, channel.qualifiedId), payload, this.disposalToken));
            }
        } else if (channel.isAccepted) {
            throw new Error("Channel is already accepted.");
        } else if (channel.isRejectedOrCanceled) {
            throw new Error("Channel is no longer available for acceptance.");
        } else {
            throw new Error("Channel could not be accepted.");
        }
    }

    /**
     * Disposes this instance if the specified promise is rejected.
     * @param promise The promise to check for failures.
     */
    protected async rejectOnFailure<T>(promise: Promise<T>) {
        try {
            await promise;
        } catch (err) {
            this._completionSource.reject(err);
        }
    }

    protected removeChannelFromOfferedQueue(channel: ChannelClass) {
        if (channel.name) {
            removeFromQueue(channel, this.channelsOfferedByThemByName[channel.name]);
        }
    }

    protected getOpenChannel(qualifiedId: QualifiedChannelId): ChannelClass | undefined {
        return this.getChannelCollection(qualifiedId.source)[qualifiedId.id];
    }

    protected setOpenChannel(channel: ChannelClass) {
        this.getChannelCollection(channel.qualifiedId.source)[channel.qualifiedId.id] = channel;
    }

    protected deleteOpenChannel(qualifiedId: QualifiedChannelId) {
        delete this.getChannelCollection(qualifiedId.source)[qualifiedId.id];
    }

    private getChannelCollection(source: ChannelSource): { [id: number]: ChannelClass } {
        switch (source) {
            case ChannelSource.Local:
                return this.locallyOfferedOpenChannels;
            case ChannelSource.Remote:
                return this.remotelyOfferedOpenChannels;
            case ChannelSource.Seeded:
                return this.seededOpenChannels;
        }
    }

    /**
     * Cancels a prior call to acceptChannelAsync
     * @param channel The promise of a channel to be canceled.
     * @param name The name of the channel the caller was accepting.
     * @param reason The reason for cancellation.
     */
    private acceptChannelCanceled(channel: Deferred<ChannelClass>, name: string, reason: any) {
        if (channel.reject(new CancellationToken.CancellationError(reason))) {
            removeFromQueue(channel, this.acceptingChannels[name]);
        }
    }

    /**
     * Responds to cancellation of a prior call to offerChannelAsync.
     * @param channel The channel previously offered.
     */
    private offerChannelCanceled(channel: ChannelClass, reason: any) {
        channel.tryCancelOffer(reason);
    }

    /**
     * Gets a unique number that can be used to represent a channel.
     * @description The channel numbers increase by two in order to maintain odd or even numbers,
     * since each party is allowed to create only one or the other.
     */
    private getUnusedChannelId() {
        return this.lastOfferedChannelId += this.isOdd !== undefined ? 2 : 1;
    }
}

// tslint:disable-next-line:max-classes-per-file
export class MultiplexingStreamClass extends MultiplexingStream {
    protected lastOfferedChannelId: number;
    private readonly sendingSemaphore = new Semaphore(1);

    constructor(stream: NodeJS.ReadWriteStream, isOdd: boolean | undefined, options: MultiplexingStreamOptions) {
        super(stream, isOdd, options);

        this.lastOfferedChannelId = isOdd ? -1 : 0; // the first channel created should be 1 or 2
        this.lastOfferedChannelId += options.seededChannels?.length ?? 0;

        // Initiate reading from the transport stream. This will not end until the stream does, or we're disposed.
        // If reading the stream fails, we'll dispose ourselves.
        this.readFromStream(this.disposalToken).catch((err) => this._completionSource.reject(err));
    }

    get backpressureSupportEnabled(): boolean {
        return this.protocolMajorVersion > 1;
    }

    public async sendFrameAsync(
        header: FrameHeader,
        payload?: Buffer,
        cancellationToken: CancellationToken = CancellationToken.CONTINUE): Promise<void> {

        if (!header) {
            throw new Error("Header is required.");
        }

        await this.sendingSemaphore.use(async () => {
            cancellationToken.throwIfCancelled();
            throwIfDisposed(this);

            await this.formatter.writeFrameAsync(header, payload);
        });
    }

    /**
     * Transmits a frame over the stream.
     * @param code The op code for the channel.
     * @param channelId The ID of the channel to receive the frame.
     * @description The promise returned from this function is always resolved (not rejected)
     * since it is anticipated that callers may not be awaiting its result.
     */
    public async sendFrame(code: ControlCode, channelId: QualifiedChannelId) {
        try {
            if (this._completionSource.isCompleted) {
                // Any frames that come in after we're done are most likely frames just informing that channels are
                // being terminated, which we do not need to communicate since the connection going down implies that.
                return;
            }

            const header = new FrameHeader(code, channelId);
            await this.sendFrameAsync(header);
        } catch (error) {
            // We mustn't throw back to our caller. So report the failure by disposing with failure.
            this._completionSource.reject(error);
        }
    }

    public onChannelWritingCompleted(channel: ChannelClass) {
        // Only inform the remote side if this channel has not already been terminated.
        if (!channel.isDisposed && this.getOpenChannel(channel.qualifiedId)) {
            this.sendFrame(ControlCode.ContentWritingCompleted, channel.qualifiedId);
        }
    }

    public async onChannelDisposed(channel: ChannelClass, error : Error | null = null) {
        if (!this._completionSource.isCompleted) {
            try {
                // Determine the payload to send to the error
                let payloadToSend : Buffer = Buffer.alloc(0);
                if(this.protocolMajorVersion > 1 && error != null) {
                    // Get the formatter use to serialize the error
                    const castedFormatter = this.formatter as MultiplexingStreamV2Formatter;

                    // Get the payload using the formatter
                    payloadToSend = castedFormatter.serializeException(error);
                }

                // Create the header and send the frame to the remote side
                const frameHeader = new FrameHeader(ControlCode.ChannelTerminated, channel.qualifiedId);
                await this.sendFrameAsync(frameHeader, payloadToSend);
            } catch (err) {
                // Swallow exceptions thrown about channel disposal if the whole stream has been taken down.
                if (this.isDisposed) {
                    return;
                }

                throw err;
            }
        }
    }

    public localContentExamined(channel: ChannelClass, bytesConsumed: number) {
        const payload = this.formatter.serializeContentProcessed(bytesConsumed);
        this.rejectOnFailure(this.sendFrameAsync(new FrameHeader(ControlCode.ContentProcessed, channel.qualifiedId), payload));
    }

    private async readFromStream(cancellationToken: CancellationToken) {
        while (!this.isDisposed) {
            const frame = await this.formatter.readFrameAsync(cancellationToken);
            if (frame === null) {
                break;
            }

            frame.header.flipChannelPerspective();
            switch (frame.header.code) {
                case ControlCode.Offer:
                    this.onOffer(frame.header.requiredChannel, frame.payload);
                    break;
                case ControlCode.OfferAccepted:
                    this.onOfferAccepted(frame.header.requiredChannel, frame.payload);
                    break;
                case ControlCode.Content:
                    this.onContent(frame.header.requiredChannel, frame.payload);
                    break;
                case ControlCode.ContentProcessed:
                    this.onContentProcessed(frame.header.requiredChannel, frame.payload);
                    break;
                case ControlCode.ContentWritingCompleted:
                    this.onContentWritingCompleted(frame.header.requiredChannel);
                    break;
                case ControlCode.ChannelTerminated:
                    this.onChannelTerminated(frame.header.requiredChannel, frame.payload);
                    break;
                default:
                    break;
            }
        }

        this.dispose();
    }

    private onOffer(channelId: QualifiedChannelId, payload: Buffer) {
        const offerParameters = this.formatter.deserializeOfferParameters(payload);
        const channel = new ChannelClass(this, channelId, offerParameters);
        let acceptingChannelAlreadyPresent = false;
        let options: ChannelOptions | undefined;

        let acceptingChannels: Deferred<ChannelClass>[];
        if ((acceptingChannels = this.acceptingChannels[offerParameters.name]) !== undefined) {
            while (acceptingChannels.length > 0) {
                const candidate = acceptingChannels.shift()!;
                if (candidate.resolve(channel)) {
                    acceptingChannelAlreadyPresent = true;
                    options = candidate.state as ChannelOptions;
                    break;
                }
            }
        }

        if (!acceptingChannelAlreadyPresent) {
            if (offerParameters.name != null) {
                let offeredChannels: Channel[];
                if (!(offeredChannels = this.channelsOfferedByThemByName[offerParameters.name])) {
                    this.channelsOfferedByThemByName[offerParameters.name] = offeredChannels = [];
                }

                offeredChannels.push(channel);
            }
        }

        this.setOpenChannel(channel);

        if (acceptingChannelAlreadyPresent) {
            this.acceptChannelOrThrow(channel, options);
        }

        this.raiseChannelOffered(channel.id, channel.name, acceptingChannelAlreadyPresent);
    }

    private onOfferAccepted(channelId: QualifiedChannelId, payload: Buffer) {
        if (channelId.source !== ChannelSource.Local) {
            throw new Error("Remote party tried to accept a channel that they offered.");
        }

        const acceptanceParameter = this.formatter.deserializeAcceptanceParameters(payload);
        const channel = this.getOpenChannel(channelId);
        if (!channel) {
            throw new Error(`Unexpected channel created with ID ${QualifiedChannelId.toString(channelId)}`);
        }

        if (!channel.onAccepted(acceptanceParameter)) {
            // This may be an acceptance of a channel that we canceled an offer for, and a race condition
            // led to our cancellation notification crossing in transit with their acceptance notification.
            // In this case, do nothing since we already sent a channel termination message, and the remote side
            // should notice it soon.
        }
    }

    private onContent(channelId: QualifiedChannelId, payload: Buffer) {
        const channel = this.getOpenChannel(channelId);
        if (!channel) {
            throw new Error(`No channel with id ${channelId} found.`);
        }

        channel.onContent(payload);
    }

    private onContentProcessed(channelId: QualifiedChannelId, payload: Buffer) {
        const channel = this.getOpenChannel(channelId);
        if (!channel) {
            throw new Error(`No channel with id ${channelId} found.`);
        }

        const bytesProcessed = this.formatter.deserializeContentProcessed(payload);
        channel.onContentProcessed(bytesProcessed);
    }

    private onContentWritingCompleted(channelId: QualifiedChannelId) {
        const channel = this.getOpenChannel(channelId);
        if (!channel) {
            throw new Error(`No channel with id ${channelId} found.`);
        }

        channel.onContent(null); // signify that the remote is done writing.
    }

    /**
     * Occurs when the remote party has terminated a channel (including canceling an offer).
     * @param channelId The ID of the terminated channel.
     */
    private onChannelTerminated(channelId: QualifiedChannelId, payload: Buffer) {
        const channel = this.getOpenChannel(channelId);
        if (channel) {
            this.deleteOpenChannel(channelId);
            this.removeChannelFromOfferedQueue(channel);

            // Extract the exception that we received from the remote side
            let remoteException : Error | null = null;
            if(this.protocolMajorVersion > 1 && payload.length > 0) {
                // Get the formatter
                const castedFormatter = this.formatter as MultiplexingStreamV2Formatter;

                // Extract the error using the casted formatter
                remoteException = castedFormatter.deserializeException(payload);
            }

            // Dispose the channel
            channel.dispose(remoteException);
        }
    }
}
