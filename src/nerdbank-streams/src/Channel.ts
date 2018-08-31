import { Duplex } from "stream";
import { ChannelOptions } from "./ChannelOptions";
import { ControlCode } from "./ControlCode";
import { Deferred } from "./Deferred";
import { FrameHeader } from "./FrameHeader";
import { IDisposableObservable } from "./IDisposableObservable";
import { MultiplexingStreamClass } from "./MultiplexingStream";

export abstract class Channel implements IDisposableObservable {
    public duplex: Duplex;
    public acceptance: Promise<void>;
    public completion: Promise<void>;
    private _isDisposed: boolean = false;

    public get isDisposed(): boolean {
        return this._isDisposed;
    }

    public dispose() {
        this._isDisposed = true;
    }
}

// tslint:disable-next-line:max-classes-per-file
export class ChannelClass extends Channel {
    public readonly id: number;
    private _duplex: Duplex;
    private readonly _acceptance = new Deferred<void>();
    private readonly _completion = new Deferred<void>();

    constructor(
        private multiplexingStream: MultiplexingStreamClass,
        id: number,
        public name: string,
        private offeredByThem: boolean,
        private options: ChannelOptions) {

        super();
        const self = this;
        this.id = id;
        this._duplex = new Duplex({
            async write(chunk, encoding, callback) {
                let error;
                try {
                    const payload = Buffer.from(chunk);

                    const header = new FrameHeader();
                    header.code = ControlCode.Content;
                    header.channelId = id;
                    header.framePayloadLength = payload.length;
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

    public get duplex(): Duplex {
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

    public onAccepted(): boolean {
        return this._acceptance.resolve();
    }

    public onContent(buffer: Buffer) {
        this._duplex.push(buffer);
    }

    public dispose() {
        if (!this.isDisposed) {
            super.dispose();

            this._acceptance.reject("canceled");

            // For the pipes, we Complete *our* ends, and leave the user's ends alone.
            // The completion will propagate when it's ready to.
            this.duplex.end();
            this.duplex.push(null);

            this._completion.resolve();
            this.multiplexingStream.onChannelDisposed(this);
        }
    }
}
