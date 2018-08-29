import { Deferred } from './Deferred';

export class MultiplexingStream {
    private readonly _completionSource : Deferred<void>;

    private _isDisposed: boolean = false;

    private constructor() {
        this._completionSource = new Deferred<void>();
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
        return this._isDisposed;
    }

    /**
     * Initializes a new instance of the MultiplexingStream class.
     */
    public static async CreateAsync() : Promise<MultiplexingStream> {
        return new MultiplexingStream();
    };

    /**
     * Disposes the stream.
     */
    public dispose() {
        this._isDisposed = true;
        this._completionSource.resolve();
    }
}
