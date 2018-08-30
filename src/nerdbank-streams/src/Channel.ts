import { Duplex } from "stream";
import { Deferred } from "./Deferred";
import { IDisposableObservable } from './IDisposableObservable';
import { MultiplexingStreamClass } from "./MultiplexingStream";
import { ChannelOptions } from "./ChannelOptions";
import { FrameHeader } from "./FrameHeader";
import { ControlCode } from "./ControlCode";

export interface Channel extends IDisposableObservable {
    duplex: Duplex;
    acceptance: Promise<void>;
    completion: Promise<void>;
}

export class ChannelClass implements Channel {
    readonly id: number;
    private _isDisposed: boolean = false;
    private _duplex: Duplex;
    private readonly _acceptance = new Deferred<void>();
    private readonly _completion = new Deferred<void>();

    constructor(private multiplexingStream: MultiplexingStreamClass, id: number, private name: string, private offeredByThem: boolean, private options: ChannelOptions) {
        var self = this;
        this.id = id;
        this._duplex = new Duplex({
            async write(chunk, encoding, callback) {
                var error = undefined;
                try {
                    var payload = Buffer.from(chunk);

                    var header = new FrameHeader();
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
                var error = undefined;
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
            }
        });
    }

    get isDisposed(): boolean {
        return this._isDisposed;
    }

    get duplex(): Duplex {
        return this._duplex;
    }

    get acceptance(): Promise<void> {
        return this._acceptance.promise;
    }

    get acceptanceIsCompleted() {
        return this._acceptance.isCompleted;
    }

    get isAccepted() {
        return this._acceptance.isCompleted
    }

    get isRejectedOrCanceled() {
        return this._acceptance.isRejected;
    }

    get completion(): Promise<void> {
        return this._completion.promise;
    }

    tryAcceptOffer(options?: ChannelOptions): boolean {
        if (this._acceptance.resolve()) {
            return true;
        }

        return false;
    }

    onAccepted() {
        return this._acceptance.resolve();
    }

    onContent(buffer: Buffer) {
        this._duplex.push(buffer);
    }

    dispose() {
        if (!this.isDisposed) {
            this._isDisposed = true;

            this._acceptance.reject("canceled");

            // For the pipes, we Complete *our* ends, and leave the user's ends alone. The completion will propagate when it's ready to.
            this.duplex.end();
            this.duplex.push(null);

            this._completion.resolve();
            this.multiplexingStream.onChannelDisposed(this);
        }
    }
}
