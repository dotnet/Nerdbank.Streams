import { CancellationToken } from 'vscode-jsonrpc';
import { Deferred } from './Deferred';
import { IDisposableObservable } from './IDisposableObservable';

export async function getBufferFrom(readable: NodeJS.ReadableStream, size: number, allowEndOfStream?: boolean, cancellationToken?: CancellationToken): Promise<Buffer> {
    while (size > 0) {
        var readBuffer = <Buffer>readable.read(size);
        if (readBuffer === null) {
            var bytesAvailable = new Deferred<void>();
            readable.once('readable', bytesAvailable.resolve.bind(bytesAvailable));
            await bytesAvailable.promise;
            continue;
        }

        if (allowEndOfStream && readBuffer.length === 0) {
            return readBuffer;
        }

        if (readBuffer.length < size) {
            throw new Error("Stream terminated before required bytes were read.");
        }

        return readBuffer;
    }
}

export function throwIfDisposed(value: IDisposableObservable) {
    if (value.isDisposed) {
        throw new Error("disposed");
    }
}
