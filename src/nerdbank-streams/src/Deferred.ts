/**
 * A TaskCompletionSource-like class that allows promises to be resolved or rejected whenever.
 */
export class Deferred<T> {
    public readonly promise: Promise<T>;
    private resolvePromise: (value?: T | PromiseLike<T>) => void;
    private rejectPromise: (reason?: any) => void;
    private _isResolved: boolean = false;
    private _isRejected: boolean = false;
    private _error: any;

    constructor(public state?: any) {
        this.promise = new Promise<T>((resolve, reject) => {
            this.resolvePromise = resolve;
            this.rejectPromise = reject;
        });
    }

    /**
     * Gets a value indicating whether this promise has been completed.
     */
    public get isCompleted() {
        return this._isResolved || this._isRejected;
    }

    /**
     * Gets a value indicating whether this promise is resolved.
     */
    public get isResolved() {
        return this._isResolved;
    }

    /**
     * Gets a value indicating whether this promise is rejected.
     */
    public get isRejected() {
        return this._isRejected;
    }

    /**
     * Gets the reason for promise rejection, if applicable.
     */
    public get error() {
        return this._error;
    }

    /**
     * Resolves the promise.
     * @param value The result of the promise.
     */
    public resolve(value?: T | PromiseLike<T>): boolean {
        if (this.isCompleted) {
            return false;
        }

        this.resolvePromise(value);
        this._isResolved = true;
        return true;
    }

    /**
     * Rejects the promise.
     * @param reason The reason for rejecting the promise.
     */
    public reject(reason?: any): boolean {
        if (this.isCompleted) {
            return false;
        }

        this.rejectPromise(reason);
        this._error = reason;
        this._isRejected = true;
        return true;
    }
}
