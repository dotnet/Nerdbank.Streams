import CancellationToken from "cancellationtoken";
import { Duplex } from "stream";
import { ChannelOptions } from "./ChannelOptions";
import { ControlCode } from "./ControlCode";
import { Deferred } from "./Deferred";
import { FrameHeader } from "./FrameHeader";
import { IDisposableObservable } from "./IDisposableObservable";
import { MultiplexingStreamClass } from "./MultiplexingStream";

export abstract class Channel implements IDisposableObservable {
    /**
     * The id of the channel.
     */
    public readonly id: number;

    /**
     * A read/write stream used to communicate over this channel.
     */
    public stream: NodeJS.ReadWriteStream;

    /**
     * A promise that completes when this channel has been accepted/rejected by the remote party.
     */
    public acceptance: Promise<void>;

    /**
     * A promise that completes when this channel is closed.
     */
    public completion: Promise<void>;

    private _isDisposed: boolean = false;

    constructor(id: number) {
        this.id = id;
    }

    /**
     * Gets a value indicating whether this channel has been disposed.
     */
    public get isDisposed(): boolean {
        return this._isDisposed;
    }

    /**
     * Closes this channel.
     */
    public dispose() {
        // The interesting stuff is in the derived class.
        this._isDisposed = true;
    }
}

// tslint:disable-next-line:max-classes-per-file
export class ChannelClass extends Channel {
    public readonly name: string;
    private _duplex: Duplex;
    private readonly _multiplexingStream: MultiplexingStreamClass;
    private readonly _acceptance = new Deferred<void>();
    private readonly _completion = new Deferred<void>();

    constructor(
        multiplexingStream: MultiplexingStreamClass,
        id: number,
        name: string,
        options: ChannelOptions) {

        super(id);
        const self = this;
        this.name = name;
        this._multiplexingStream = multiplexingStream;
        this._duplex = new Duplex({
            async write(chunk, encoding, callback) {
                let error;
                try {
                    const payload = Buffer.from(chunk);

                    const header = new FrameHeader(ControlCode.Content, id, payload.length);
                    await multiplexingStream.sendFrameAsync(header, payload);
                } catch (err) {
                    error = err;
                }

                if (callback) {
                    callback(error);
                }
            },

            async final(cb?: (err?: any) => void) {
                let error;
                try {
                    await multiplexingStream.onChannelWritingCompleted(self);
                } catch (err) {
                    error = err;
                }

                if (cb) {
                    cb(error);
                }
            },

            read(size) {
                // Nothing to do here since data is pushed to us.
            },
        });
    }

    public get stream(): NodeJS.ReadWriteStream {
        return this._duplex;
    }

    public get acceptance(): Promise<void> {
        return this._acceptance.promise;
    }

    public get isAccepted() {
        return this._acceptance.isResolved;
    }

    public get isRejectedOrCanceled() {
        return this._acceptance.isRejected;
    }

    public get completion(): Promise<void> {
        return this._completion.promise;
    }

    public tryAcceptOffer(options?: ChannelOptions): boolean {
        if (this._acceptance.resolve()) {
            return true;
        }

        return false;
    }

    public tryCancelOffer(reason: any) {
        const cancellationReason = new CancellationToken.CancellationError(reason);
        this._acceptance.reject(cancellationReason);
        this._completion.reject(cancellationReason);
    }

    public onAccepted(): boolean {
        return this._acceptance.resolve();
    }

    public onContent(buffer: Buffer) {
        this._duplex.push(buffer);
    }

    public dispose() {
        if (!this.isDisposed) {
            super.dispose();

            this._acceptance.reject(new CancellationToken.CancellationError("disposed"));

            // For the pipes, we Complete *our* ends, and leave the user's ends alone.
            // The completion will propagate when it's ready to.
            this._duplex.end();
            this._duplex.push(null);

            this._completion.resolve();
            this._multiplexingStream.onChannelDisposed(this);
        }
    }
}
