import { CancellationToken } from 'vscode-jsonrpc';
import { Deferred } from './Deferred';
import { IDisposableObservable } from './IDisposableObservable';

export async function getBufferFrom(readable: NodeJS.ReadableStream, size: number, allowEndOfStream?: boolean, cancellationToken?: CancellationToken): Promise<Buffer> {
    var streamEnded = new Deferred<void>();
    while (size > 0) {
        var readBuffer = <Buffer>readable.read(size);
        if (readBuffer === null) {
            var bytesAvailable = new Deferred<void>();
            readable.once('readable', bytesAvailable.resolve.bind(bytesAvailable));
            readable.once('end', streamEnded.resolve.bind(streamEnded));
            await Promise.race([bytesAvailable.promise, streamEnded.promise]);
            if (!streamEnded.isCompleted) {
                continue;
            }
        }

        if (!allowEndOfStream) {
            if (!readBuffer || readBuffer.length < size) {
                throw new Error("Stream terminated before required bytes were read.");
            }
        }

        return readBuffer || new Buffer([]);
    }
}

export function throwIfDisposed(value: IDisposableObservable) {
    if (value.isDisposed) {
        throw new Error("disposed");
    }
}
