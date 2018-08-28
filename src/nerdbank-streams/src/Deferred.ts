/**
 * A TaskCompletionSource-like class that allows promises to be resolved or rejected whenever.
 */
export class Deferred<T> {
    readonly promise: Promise<T>;
    resolve: (value?: T | PromiseLike<T>) => void;
    reject: (reason?: any) => void;

    constructor() {
        this.promise = new Promise<T>((resolve, reject) => {
            this.resolve = resolve;
            this.reject = reject;
        });
    }
}
