export interface IDisposableObservable {
    readonly isDisposed: boolean;
    dispose();
}
