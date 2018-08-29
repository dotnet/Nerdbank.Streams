import { Deferred } from "../Deferred";

export function timeout<T>(promise: Promise<T>, timeout: number) : Promise<T> {
    var deferred = new Deferred<T>();
    var timer = setTimeout(
        () => {
            deferred.reject('timeout expired');
        },
        timeout);
    promise.then(result => {
        clearTimeout(timer);
        deferred.resolve(result);
    });
    promise.catch(reason => {
        clearTimeout(timer);
        deferred.reject(reason);
    });
    return deferred.promise;
};
