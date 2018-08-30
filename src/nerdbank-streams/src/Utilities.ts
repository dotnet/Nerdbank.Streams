import { CancellationToken } from "vscode-jsonrpc";
import { Deferred } from "./Deferred";
import { IDisposableObservable } from "./IDisposableObservable";

export async function getBufferFrom(
    readable: NodeJS.ReadableStream,
    size: number,
    allowEndOfStream?: boolean,
    cancellationToken?: CancellationToken): Promise<Buffer> {

    const streamEnded = new Deferred<void>();
    while (size > 0) {
        const readBuffer = readable.read(size) as Buffer;
        if (readBuffer === null) {
            const bytesAvailable = new Deferred<void>();
            readable.once("readable", bytesAvailable.resolve.bind(bytesAvailable));
            readable.once("end", streamEnded.resolve.bind(streamEnded));
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
