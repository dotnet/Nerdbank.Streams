import CancellationToken from "cancellationtoken";
import { Readable, Writable } from "stream";
import { IDisposableObservable } from "./IDisposableObservable";

export async function writeAsync(stream: NodeJS.WritableStream, chunk: any) {
    return new Promise<void>((resolve, reject) => {
        stream.write(chunk, (err: Error | null | undefined) => {
            if (err) {
                reject(err);
            } else {
                resolve();
            }
        });
    });
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
                callback(err as Error);
            }
        },
        final(callback: (error?: Error | null) => void) {
            // Write the terminating 0 length sequence.
            stream.write(new Uint8Array(4), callback);
        },
    });
}

/**
 * Reads the next chunk from a stream, asynchronously waiting for more to be read if necessary.
 * @param stream The stream to read from.
 * @param cancellationToken A token whose cancellation will result in immediate rejection of the previously returned promise.
 * @returns The result of reading from the stream. This will be null if the end of the stream is reached before any more can be read.
 */
export function readAsync(stream: NodeJS.ReadableStream, cancellationToken?: CancellationToken): Promise<string | Buffer | null> {
    if (!(stream.isPaused() || (stream as Readable).readableFlowing !== true)) {
        throw new Error('Stream must not be in flowing mode.');
    }

    let result = stream.read()
    if (result) {
        return Promise.resolve(result)
    }

    return new Promise<string | Buffer | null>((resolve, reject) => {
        const ctReg = cancellationToken?.onCancelled(reason => {
            cleanup();
            reject(reason);
        });
        stream.once('data', onData);
        stream.once('error', onError);
        stream.once('end', onEnd);
        stream.resume()

        function onData(chunk) {
            cleanup();
            resolve(chunk);
        }

        function onError(...args) {
            cleanup();
            reject(...args);
        }

        function onEnd() {
            cleanup();
            resolve(null);
        }

        function cleanup() {
            stream.pause();
            stream.off('data', onData);
            stream.off('error', onError);
            stream.off('end', onEnd);
            if (ctReg) {
                ctReg();
            }
        }
    })
}

/**
 * Returns a readable stream that will read just a slice of some existing stream.
 * @param stream The stream to read from.
 * @param length The maximum number of bytes to read from the stream.
 * @returns A stream that will read up to the given number of elements, leaving the rest in the underlying stream.
 */
export function sliceStream(stream: NodeJS.ReadableStream, length: number): Readable {
    return new Readable({
        async read(_: number) {
            if (length > 0) {
                const chunk = await readAsync(stream);
                if (!chunk) {
                    // We've reached the end of the source stream.
                    this.push(null);
                    return;
                }

                const countToConsume = Math.min(length, chunk.length)
                length -= countToConsume
                stream.unshift(chunk.slice(countToConsume))
                this.push(chunk.slice(0, countToConsume))
            } else {
                this.push(null);
            }
        },
    });
}

export function readSubstream(stream: NodeJS.ReadableStream): NodeJS.ReadableStream {
    let currentSlice: Readable | null = null
    return new Readable({
        async read(_: number) {
            while (true) {
                if (currentSlice === null) {
                    const lenBuffer = await getBufferFrom(stream, 4);
                    const dv = new DataView(lenBuffer.buffer, lenBuffer.byteOffset, lenBuffer.length);
                    const length = dv.getUint32(0, false);
                    if (length === 0) {
                        // We've reached the end of the substream.
                        this.push(null);
                        return;
                    }

                    currentSlice = sliceStream(stream, length)
                }

                const chunk = await readAsync(currentSlice);
                if (!chunk) {
                    // We've reached the end of this chunk. We'll have to read the next header.
                    currentSlice = null;
                    continue;
                }

                this.push(chunk);
                return;
            }
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

    if (size === 0) {
        return Buffer.alloc(0)
    }

    const initialData = readable.read(size) as Buffer | null;
    if (initialData) {
        return initialData;
    }

    let totalBytesRead = 0
    const result = Buffer.alloc(size);
    const streamSlice = sliceStream(readable, size);
    while (totalBytesRead < size) {
        const chunk = await readAsync(streamSlice, cancellationToken) as Buffer | null
        if (chunk === null) {
            // We reached the end prematurely.
            if (allowEndOfStream) {
                return totalBytesRead === 0 ? null : result.subarray(0, totalBytesRead)
            } else {
                throw new Error(`End of stream encountered after only ${totalBytesRead} bytes when ${size} were expected.`);
            }
        }

        chunk.copy(result, totalBytesRead);
        totalBytesRead += chunk.length;
    }

    return result;
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
