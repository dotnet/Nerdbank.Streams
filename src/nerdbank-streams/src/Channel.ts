import { Duplex } from "stream";
import { Deferred } from "./Deferred";
import { IDisposableObservable } from './IDisposableObservable';

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

    constructor() {
        this._duplex = new Duplex({
            write(chunk, encoding, callback) {

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

    get completion(): Promise<void> {
        return this._completion.promise;
    }

    public dispose() {
        this._isDisposed = true;
    }
}
