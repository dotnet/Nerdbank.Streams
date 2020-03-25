import CancellationToken from "cancellationtoken";
import { Readable, Writable } from "stream";
import { Deferred } from "./Deferred";
import { IDisposableObservable } from "./IDisposableObservable";

export async function writeAsync(stream: NodeJS.WritableStream, chunk: any) {
    const deferred = new Deferred<void>();
    stream.write(chunk, (err: Error | null | undefined) => {
        if (err) {
            deferred.reject(err);
        } else {
            deferred.resolve();
        }
    });
    return deferred.promise;
}

export function writeSubstream(stream: NodeJS.WritableStream): NodeJS.WritableStream {
    return new Writable({
        async write(chunk: Buffer, _: string, callback: (error?: Error | null) => void) {
            try {
                const dv = new DataView(new ArrayBuffer(4));
                dv.setUint32(0, chunk.length, false);
                await writeAsync(stream, Buffer.from(dv.buffer));
                await writeAsync(stream, chunk);
                callback();
            } catch (err) {
                callback(err);
            }
        },
        final(callback: (error?: Error | null) => void) {
            // Write the terminating 0 length sequence.
            stream.write(new Uint8Array(4), callback);
        },
    });
}

export function readSubstream(stream: NodeJS.ReadableStream): NodeJS.ReadableStream {
    return new Readable({
        async read(_: number) {
            const lenBuffer = await getBufferFrom(stream, 4);
            const dv = new DataView(lenBuffer.buffer, lenBuffer.byteOffset, lenBuffer.length);
            const chunkSize = dv.getUint32(0, false);
            if (chunkSize === 0) {
                this.push(null);
                return;
            }

            // TODO: make this *stream* instead of read as an atomic chunk.
            const payload = await getBufferFrom(stream, chunkSize);
            this.push(payload);
        },
    });
}

export async function getBufferFrom(
    readable: NodeJS.ReadableStream,
    size: number,
    allowEndOfStream?: false,
    cancellationToken?: CancellationToken): Promise<Buffer>;

export async function getBufferFrom(
    readable: NodeJS.ReadableStream,
    size: number,
    allowEndOfStream: true,
    cancellationToken?: CancellationToken): Promise<Buffer | null>;

export async function getBufferFrom(
    readable: NodeJS.ReadableStream,
    size: number,
    allowEndOfStream: boolean = false,
    cancellationToken?: CancellationToken): Promise<Buffer | null> {

    const streamEnded = new Deferred<void>();
    while (size > 0) {
        const readBuffer = readable.read(size) as Buffer;
        if (readBuffer === null) {
            const bytesAvailable = new Deferred<void>();
            readable.once("readable", bytesAvailable.resolve.bind(bytesAvailable));
            readable.once("end", streamEnded.resolve.bind(streamEnded));
            const endPromise = Promise.race([bytesAvailable.promise, streamEnded.promise]);
            await (cancellationToken ? cancellationToken.racePromise(endPromise) : endPromise);

            if (bytesAvailable.isCompleted) {
                continue;
            }
        }

        if (!allowEndOfStream) {
            if (!readBuffer || readBuffer.length < size) {
                throw new Error("Stream terminated before required bytes were read.");
            }
        }

        return readBuffer;
    }

    return new Buffer([]);
}

export function throwIfDisposed(value: IDisposableObservable) {
    if (value.isDisposed) {
        throw new Error("disposed");
    }
}

export function requireInteger(
    parameterName: string,
    value: number,
    serializedByteLength: number,
    signed: "unsigned" | "signed" = "signed"): void {

    if (!Number.isInteger(value)) {
        throw new Error(`${parameterName} must be an integer.`);
    }

    let bits = serializedByteLength * 8;
    if (signed === "signed") {
        bits--;
    }

    const maxValue = Math.pow(2, bits) - 1;
    const minValue = signed === "signed" ? -Math.pow(2, bits) : 0;
    if (value > maxValue || value < minValue) {
        throw new Error(`${parameterName} must be in the range ${minValue}-${maxValue}.`);
    }
}

export function removeFromQueue<T>(value: T, queue: T[]) {
    if (queue) {
        const idx = queue.indexOf(value);
        if (idx >= 0) {
            queue.splice(idx, 1);
        }
    }
}
