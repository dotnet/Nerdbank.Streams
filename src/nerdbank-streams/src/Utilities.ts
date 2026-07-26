import CancellationToken from 'cancellationtoken'
import { Readable, Writable } from 'stream'
import { IDisposableObservable } from './IDisposableObservable'

export async function writeAsync(stream: NodeJS.WritableStream, chunk: any) {
	return new Promise<void>((resolve, reject) => {
		stream.write(chunk, (err: Error | null | undefined) => {
			if (err) {
				reject(err)
			} else {
				resolve()
			}
		})
	})
}

export function writeSubstream(stream: NodeJS.WritableStream): NodeJS.WritableStream {
	return new Writable({
		async write(chunk: Buffer, _: string, callback: (error?: Error | null) => void) {
			try {
				const dv = new DataView(new ArrayBuffer(4))
				dv.setUint32(0, chunk.length, false)
				await writeAsync(stream, Buffer.from(dv.buffer))
				await writeAsync(stream, chunk)
				callback()
			} catch (err) {
				callback(err as Error)
			}
		},
		final(callback: (error?: Error | null) => void) {
			// Write the terminating 0 length sequence.
			stream.write(new Uint8Array(4), callback)
		},
	})
}

/**
 * Reads the next chunk from a stream, asynchronously waiting for more to be read if necessary.
 * @param stream The stream to read from.
 * @param cancellationToken A token whose cancellation will result in immediate rejection of the returned promise.
 * @returns The result of reading from the stream. This will be null if the end of the stream is reached before any more can be read.
 */
export function readAsync(stream: NodeJS.ReadableStream, cancellationToken?: CancellationToken): Promise<string | Buffer | null> {
	return readAtMostAsync(stream, undefined, cancellationToken)
}

/**
 * Reads the next chunk from a stream, asynchronously waiting for more to be read if necessary,
 * without ever consuming more than a given number of bytes from the stream.
 * @param stream The stream to read from.
 * @param maxBytes The maximum number of bytes to consume from the stream, or `undefined` to consume whatever is immediately available.
 * @param cancellationToken A token whose cancellation will result in immediate rejection of the returned promise.
 * @returns The result of reading from the stream. This will be null if the end of the stream is reached before any more can be read.
 */
function readAtMostAsync(stream: NodeJS.ReadableStream, maxBytes?: number, cancellationToken?: CancellationToken): Promise<string | Buffer | null> {
	if (cancellationToken?.isCancelled) {
		return Promise.reject(new CancellationToken.CancellationError(cancellationToken.reason))
	}

	// Always try a synchronous read first. Besides being faster, this avoids resuming on a later
	// microtask, by which time the stream may have already emitted its 'end' event.
	const initialChunk = readSyncAtMost(stream, maxBytes)
	if (initialChunk !== null) {
		return Promise.resolve(initialChunk)
	}

	const readable = stream as Readable
	if (readable.errored) {
		// The stream has already failed. Report it immediately rather than waiting for an
		// 'error' event that may have already been emitted.
		return Promise.reject(readable.errored)
	}

	if (readable.readableEnded || readable.destroyed) {
		return Promise.resolve(null)
	}

	return new Promise<string | Buffer | null>((resolve, reject) => {
		// Note that adding a 'readable' event handler switches the stream to paused mode, which is exactly what we want
		// since we read explicitly. Node.js restores the stream's prior flowing state when the last such handler is removed,
		// so this has no lasting impact on the stream for other consumers.
		const ctReg = cancellationToken?.onCancelled(reason => {
			cleanup()
			reject(new CancellationToken.CancellationError(reason))
		})
		stream.on('readable', onReadable)
		stream.once('error', onError)
		stream.once('end', onEnd)
		stream.once('close', onClose)

		function onReadable() {
			const chunk = readSyncAtMost(stream, maxBytes)
			if (chunk !== null) {
				cleanup()
				resolve(chunk)
			}
		}

		function onError(err: Error) {
			cleanup()
			reject(err)
		}

		function onEnd() {
			cleanup()
			resolve(null)
		}

		function onClose() {
			cleanup()
			resolve(null)
		}

		function cleanup() {
			stream.off('readable', onReadable)
			stream.off('error', onError)
			stream.off('end', onEnd)
			stream.off('close', onClose)
			if (ctReg) {
				ctReg()
			}
		}
	})
}

/**
 * Synchronously reads whatever is immediately available from a stream, without consuming more than `maxBytes` bytes.
 * @param stream The stream to read from.
 * @param maxBytes The maximum number of bytes to consume, or `undefined` for no limit.
 * @returns The chunk that was read, or null if nothing was immediately available.
 */
function readSyncAtMost(stream: NodeJS.ReadableStream, maxBytes?: number): string | Buffer | null {
	if (maxBytes === undefined) {
		return stream.read() as string | Buffer | null
	}

	if (maxBytes === 0) {
		return null
	}

	// Only ask for as much as is already buffered so that `read` doesn't return null merely
	// because fewer than `maxBytes` bytes have arrived so far.
	const buffered = (stream as Readable).readableLength
	return stream.read(buffered > 0 ? Math.min(buffered, maxBytes) : maxBytes) as string | Buffer | null
}

/**
 * Returns a readable stream that will read just a slice of some existing stream.
 * @param stream The stream to read from.
 * @param length The maximum number of bytes to read from the stream.
 * @returns A stream that will read up to the given number of bytes, leaving the rest in the underlying stream.
 */
export function sliceStream(stream: NodeJS.ReadableStream, length: number): Readable {
	// Reads that are in flight when this stream is destroyed must be cancelled so that
	// they do not go on to consume data from the underlying stream that no one will receive.
	const cts = CancellationToken.create()
	return new Readable({
		async read(_: number) {
			try {
				if (length === 0) {
					this.push(null)
					return
				}

				const chunk = (await readAtMostAsync(stream, length, cts.token)) as Buffer | null
				if (chunk === null) {
					// We've reached the end of the source stream.
					this.push(null)
					return
				}

				length -= chunk.length
				this.push(chunk)
				if (length === 0) {
					// Save another call later by informing immediately that we're at the end of the stream.
					this.push(null)
				}
			} catch (err) {
				this.destroy(err as Error)
			}
		},

		destroy(error, callback) {
			cts.cancel()
			callback(error)
		},
	})
}

/**
 * Returns a readable stream that reads a substream that was written with {@link writeSubstream}.
 * @param stream The stream to read the substream from.
 * @returns A stream that ends when the substream ends, leaving the rest of `stream` available to other readers.
 */
export function readSubstream(stream: NodeJS.ReadableStream): Readable {
	let bytesRemainingInChunk = 0
	let reachedEnd = false

	// Reads that are in flight when this stream is destroyed must be cancelled so that
	// they do not go on to consume data from the underlying stream that no one will receive.
	const cts = CancellationToken.create()
	return new Readable({
		async read(_: number) {
			try {
				if (reachedEnd) {
					this.push(null)
					return
				}

				if (bytesRemainingInChunk === 0) {
					const lenBuffer = await getBufferFrom(stream, 4, false, cts.token)
					const dv = new DataView(lenBuffer.buffer, lenBuffer.byteOffset, lenBuffer.length)
					bytesRemainingInChunk = dv.getUint32(0, false)
					if (bytesRemainingInChunk === 0) {
						// We've reached the end of the substream.
						reachedEnd = true
						this.push(null)
						return
					}
				}

				// Push whatever is available rather than waiting for the entire chunk to arrive.
				const payload = (await readAtMostAsync(stream, bytesRemainingInChunk, cts.token)) as Buffer | null
				if (payload === null) {
					throw new Error('Stream terminated before the substream was completed.')
				}

				bytesRemainingInChunk -= payload.length
				this.push(payload)
			} catch (err) {
				this.destroy(err as Error)
			}
		},

		destroy(error, callback) {
			cts.cancel()
			callback(error)
		},
	})
}

export async function getBufferFrom(
	readable: NodeJS.ReadableStream,
	size: number,
	allowEndOfStream?: false,
	cancellationToken?: CancellationToken
): Promise<Buffer>

export async function getBufferFrom(
	readable: NodeJS.ReadableStream,
	size: number,
	allowEndOfStream: true,
	cancellationToken?: CancellationToken
): Promise<Buffer | null>

export async function getBufferFrom(
	readable: NodeJS.ReadableStream,
	size: number,
	allowEndOfStream: boolean = false,
	cancellationToken?: CancellationToken
): Promise<Buffer | null> {
	if (size === 0) {
		return Buffer.alloc(0)
	}

	let result: Buffer | null = null
	let bytesRead = 0
	while (bytesRead < size) {
		cancellationToken?.throwIfCancelled()
		const chunk = (await readAtMostAsync(readable, size - bytesRead, cancellationToken)) as Buffer | null
		if (chunk === null) {
			// The stream ended before we could read everything that was requested.
			if (!allowEndOfStream) {
				throw new Error('Stream terminated before required bytes were read.')
			}

			return bytesRead === 0 ? null : result!.subarray(0, bytesRead)
		}

		if (result === null && chunk.length === size) {
			// Fast path: the entire request was satisfied by a single read, so avoid an extra allocation and copy.
			return chunk
		}

		if (result === null) {
			result = Buffer.alloc(size)
		}

		chunk.copy(result, bytesRead)
		bytesRead += chunk.length
	}

	return result
}

export function throwIfDisposed(value: IDisposableObservable) {
	if (value.isDisposed) {
		throw new Error('disposed')
	}
}

export function requireInteger(parameterName: string, value: number, serializedByteLength: number, signed: 'unsigned' | 'signed' = 'signed'): void {
	if (!Number.isInteger(value)) {
		throw new Error(`${parameterName} must be an integer.`)
	}

	let bits = serializedByteLength * 8
	if (signed === 'signed') {
		bits--
	}

	const maxValue = Math.pow(2, bits) - 1
	const minValue = signed === 'signed' ? -Math.pow(2, bits) : 0
	if (value > maxValue || value < minValue) {
		throw new Error(`${parameterName} must be in the range ${minValue}-${maxValue}.`)
	}
}

export function removeFromQueue<T>(value: T, queue: T[]) {
	if (queue) {
		const idx = queue.indexOf(value)
		if (idx >= 0) {
			queue.splice(idx, 1)
		}
	}
}
