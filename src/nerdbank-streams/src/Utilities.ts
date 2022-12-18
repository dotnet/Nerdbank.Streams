import CancellationToken from 'cancellationtoken'
import { Readable, Writable } from 'stream'
import { Deferred } from './Deferred'
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

export function readSubstream(stream: NodeJS.ReadableStream): NodeJS.ReadableStream {
	return new Readable({
		async read(_: number) {
			const lenBuffer = await getBufferFrom(stream, 4)
			const dv = new DataView(lenBuffer.buffer, lenBuffer.byteOffset, lenBuffer.length)
			const chunkSize = dv.getUint32(0, false)
			if (chunkSize === 0) {
				this.push(null)
				return
			}

			// TODO: make this *stream* instead of read as an atomic chunk.
			const payload = await getBufferFrom(stream, chunkSize)
			this.push(payload)
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
	const streamEnded = new Deferred<void>()

	if (size === 0) {
		return Buffer.from([])
	}

	let readBuffer: Buffer | null = null
	let index: number = 0
	while (size > 0) {
		cancellationToken?.throwIfCancelled()
		let availableSize = (readable as Readable).readableLength
		if (!availableSize) {
			// Check the end of stream
			if ((readable as Readable).readableEnded || streamEnded.isCompleted) {
				// stream is closed
				if (!allowEndOfStream) {
					throw new Error('Stream terminated before required bytes were read.')
				}

				// Returns what has been read so far
				if (readBuffer === null) {
					return null
				}

				// we need trim extra spaces
				return readBuffer.subarray(0, index)
			}

			// we retain this behavior when availableSize === false
			// to make existing unit tests happy (which assumes we will try to read stream when no data is ready.)
			availableSize = size
		} else if (availableSize > size) {
			availableSize = size
		}

		const newBuffer = readable.read(availableSize) as Buffer
		if (newBuffer) {
			if (newBuffer.length < availableSize && !allowEndOfStream) {
				throw new Error('Stream terminated before required bytes were read.')
			}

			if (readBuffer === null) {
				if (availableSize === size || newBuffer.length < availableSize) {
					// in the fast pass, we read the entire data once, and donot allocate an extra array.
					return newBuffer
				}

				// if we read partial data, we need allocate a buffer to join all data together.
				readBuffer = Buffer.alloc(size)
			}

			// now append new data to the buffer
			newBuffer.copy(readBuffer, index)

			size -= newBuffer.length
			index += newBuffer.length
		}

		if (size > 0) {
			const bytesAvailable = new Deferred<void>()
			const bytesAvailableCallback = bytesAvailable.resolve.bind(bytesAvailable)
			const streamEndedCallback = streamEnded.resolve.bind(streamEnded)
			readable.once('readable', bytesAvailableCallback)
			readable.once('end', streamEndedCallback)
			try {
				const endPromise = Promise.race([bytesAvailable.promise, streamEnded.promise])
				await (cancellationToken ? cancellationToken.racePromise(endPromise) : endPromise)
			} finally {
				readable.removeListener('readable', bytesAvailableCallback)
				readable.removeListener('end', streamEndedCallback)
			}
		}
	}

	return readBuffer
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
