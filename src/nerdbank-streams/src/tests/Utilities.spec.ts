import CancellationToken from 'cancellationtoken'
import { PassThrough, Readable } from 'stream'
import { Deferred } from '../Deferred'
import { getBufferFrom, readAsync, sliceStream } from '../Utilities'
import { delay } from './Timeout'

let thru: PassThrough
beforeEach(() => {
	thru = new PassThrough()
})

describe('readAsync', () => {
	it('returns immediately with buffered results', async () => {
		thru.write(Buffer.from([1, 2, 3]))
		thru.write(Buffer.from([4, 5, 6]))

		const result = await readAsync(thru)
		expect(result).toEqual(Buffer.from([1, 2, 3, 4, 5, 6]))
	})

	it('waits for data', async () => {
		const resultPromise = readAsync(thru)

		thru.write(Buffer.from([1, 2, 3]))

		const result = await resultPromise
		expect(result).toEqual(Buffer.from([1, 2, 3]))
	})

	it('returns null at EOF', async () => {
		thru.end()
		expect(await readAsync(thru)).toBeNull()
	})

	it('returns null when the stream ends while waiting', async () => {
		const resultPromise = readAsync(thru)
		thru.end()
		expect(await resultPromise).toBeNull()
	})

	it('returns remaining data before EOF, then null', async () => {
		thru.end(Buffer.from([1, 2, 3]))
		expect(await readAsync(thru)).toEqual(Buffer.from([1, 2, 3]))
		expect(await readAsync(thru)).toBeNull()
		expect(await readAsync(thru)).toBeNull()
	})

	it('returns null after the end event has already been emitted', async () => {
		thru.end()
		thru.resume()
		await new Promise<void>(resolve => thru.once('end', resolve))
		expect(await readAsync(thru)).toBeNull()
	})

	it('propagates errors from an already faulted stream', async () => {
		const error = new Error('Mock error')

		// Attach an error handler as any real consumer of a stream must, since node.js
		// crashes the process when a stream emits 'error' with no listeners.
		thru.on('error', () => {})
		thru.destroy(error)
		await expect(readAsync(thru)).rejects.toThrow(error)
	})

	it('propagates errors that occur while waiting', async () => {
		const error = new Error('Mock error')
		const readPromise = readAsync(thru)
		thru.destroy(error)
		await expect(readPromise).rejects.toThrow(error)
	})

	it('returns null when the stream is destroyed without an error while waiting', async () => {
		const readPromise = readAsync(thru)
		thru.destroy()
		expect(await readPromise).toBeNull()
	})

	it('bails on cancellation', async () => {
		const cts = CancellationToken.create()
		const readPromise = readAsync(thru, cts.token)
		cts.cancel()
		await expect(readPromise).rejects.toThrow()
	})

	it('bails immediately when the token is already cancelled', async () => {
		const cts = CancellationToken.create()
		cts.cancel()
		await expect(readAsync(thru, cts.token)).rejects.toThrow()
	})

	it('does not leak event handlers', async () => {
		for (let i = 0; i < 25; i++) {
			const readPromise = readAsync(thru)
			thru.write(Buffer.from([i]))
			await readPromise
		}

		expect(thru.listenerCount('readable')).toEqual(0)
		expect(thru.listenerCount('end')).toEqual(0)
		expect(thru.listenerCount('error')).toEqual(0)
		expect(thru.listenerCount('close')).toEqual(0)
	})

	it('does not leak event handlers after cancellation', async () => {
		const cts = CancellationToken.create()
		const readPromise = readAsync(thru, cts.token)
		cts.cancel()
		await expect(readPromise).rejects.toThrow()

		expect(thru.listenerCount('readable')).toEqual(0)
		expect(thru.listenerCount('end')).toEqual(0)
		expect(thru.listenerCount('error')).toEqual(0)
		expect(thru.listenerCount('close')).toEqual(0)
	})

	// This is a regression test for the case where reading with the 'data' event (flowing mode)
	// would permanently leave the stream paused, such that a subsequent consumer that attached
	// a 'data' handler would never receive anything.
	it('leaves the stream in its original (non-flowing) state', async () => {
		expect(thru.readableFlowing).toBeNull()

		thru.write(Buffer.from([1, 2, 3]))
		expect(await readAsync(thru)).toEqual(Buffer.from([1, 2, 3]))

		// A pending read that is satisfied later must also restore the state.
		const pendingRead = readAsync(thru)
		thru.write(Buffer.from([4, 5, 6]))
		expect(await pendingRead).toEqual(Buffer.from([4, 5, 6]))

		// Wait for node to process the removal of the 'readable' handler.
		await delay(1)
		expect(thru.readableFlowing).toBeNull()

		// Now verify that a 'data' based consumer still works.
		const received = new Deferred<Buffer>()
		thru.on('data', chunk => received.resolve(chunk))
		thru.write(Buffer.from([7, 8, 9]))
		expect(await received.promise).toEqual(Buffer.from([7, 8, 9]))
	})

	it('supports interleaving with synchronous reads', async () => {
		thru.write(Buffer.from([1, 2, 3]))
		expect(thru.read(1)).toEqual(Buffer.from([1]))
		expect(await readAsync(thru)).toEqual(Buffer.from([2, 3]))
	})
})

describe('sliceStream', () => {
	it('returns null on empty', async () => {
		thru.end()
		const slice = sliceStream(thru, 5)
		expect(await readAsync(slice)).toBeNull()
	})

	it('returns subset of underlying stream', async () => {
		thru.write(Buffer.from([1, 2, 3, 4, 5, 6]))
		const slice = sliceStream(thru, 3)
		expect(await readAsync(slice)).toEqual(Buffer.from([1, 2, 3]))
		expect(await readAsync(slice)).toBeNull()
		expect(await readAsync(thru)).toEqual(Buffer.from([4, 5, 6]))
	})

	it('leaves the remainder available even when the source has ended', async () => {
		thru.end(Buffer.from([1, 2, 3, 4, 5, 6]))
		const slice = sliceStream(thru, 3)
		expect(await readAsync(slice)).toEqual(Buffer.from([1, 2, 3]))
		expect(await readAsync(slice)).toBeNull()
		expect(await readAsync(thru)).toEqual(Buffer.from([4, 5, 6]))
		expect(await readAsync(thru)).toBeNull()
	})

	it('handles slice that exceeds stream length', async () => {
		thru.end(Buffer.from([1, 2, 3]))
		const slice = sliceStream(thru, 6)

		expect(await readAsync(slice)).toEqual(Buffer.from([1, 2, 3]))
		expect(await readAsync(slice)).toBeNull()
	})

	it('handles a zero length slice', async () => {
		thru.write(Buffer.from([1, 2, 3]))
		const slice = sliceStream(thru, 0)
		expect(await readAsync(slice)).toBeNull()
		expect(await readAsync(thru)).toEqual(Buffer.from([1, 2, 3]))
	})

	it('spans multiple source chunks', async () => {
		const slice = sliceStream(thru, 5)
		const readTask = getBufferFrom(slice, 5)
		thru.write(Buffer.from([1, 2]))
		await delay(1)
		thru.write(Buffer.from([3, 4]))
		await delay(1)
		thru.write(Buffer.from([5, 6, 7]))
		expect(await readTask).toEqual(Buffer.from([1, 2, 3, 4, 5]))
		expect(await readAsync(thru)).toEqual(Buffer.from([6, 7]))
	})

	it('streams data as it arrives rather than waiting for the whole slice', async () => {
		const slice = sliceStream(thru, 6)
		const firstRead = readAsync(slice)
		thru.write(Buffer.from([1, 2, 3]))
		expect(await firstRead).toEqual(Buffer.from([1, 2, 3]))
	})

	it('can be consumed by pipe', async () => {
		thru.end(Buffer.from([1, 2, 3, 4, 5, 6]))
		const slice = sliceStream(thru, 4)
		const destination = new PassThrough()
		slice.pipe(destination)
		expect(await getBufferFrom(destination, 4)).toEqual(Buffer.from([1, 2, 3, 4]))
		expect(await readAsync(thru)).toEqual(Buffer.from([5, 6]))
	})

	it('can be consumed by async iteration', async () => {
		thru.end(Buffer.from([1, 2, 3, 4, 5, 6]))
		const slice = sliceStream(thru, 4)
		const chunks: Buffer[] = []
		for await (const chunk of slice) {
			chunks.push(chunk as Buffer)
		}

		expect(Buffer.concat(chunks)).toEqual(Buffer.from([1, 2, 3, 4]))
	})

	it('propagates errors from the source stream', async () => {
		const error = new Error('Mock error')
		const slice = sliceStream(thru, 5)
		const readTask = readAsync(slice)
		thru.destroy(error)
		await expect(readTask).rejects.toThrow(error)
	})

	it('stops consuming the source stream when destroyed', async () => {
		const slice = sliceStream(thru, 5)
		slice.on('error', () => {})

		// Start a read that cannot complete yet, then destroy the slice.
		const readTask = readAsync(slice)
		slice.destroy()
		await expect(Promise.race([readTask, delay(5).then(() => 'timeout')])).resolves.toBeDefined()
		await delay(1)

		// Data written after the slice was destroyed must remain in the source stream.
		thru.write(Buffer.from([1, 2, 3]))
		await delay(5)
		expect(thru.readableLength).toEqual(3)
		expect(await readAsync(thru)).toEqual(Buffer.from([1, 2, 3]))
		expect(thru.listenerCount('readable')).toEqual(0)
	})
})

describe('getBufferFrom', () => {
	it('returns an empty buffer when 0 bytes are requested', async () => {
		expect(await getBufferFrom(thru, 0)).toEqual(Buffer.alloc(0))
	})

	it('reads exactly the requested number of bytes', async () => {
		thru.write(Buffer.from([1, 2, 3, 4, 5]))
		expect(await getBufferFrom(thru, 2)).toEqual(Buffer.from([1, 2]))
		expect(await getBufferFrom(thru, 3)).toEqual(Buffer.from([3, 4, 5]))
	})

	it('does not consume more than requested even when the stream has ended', async () => {
		thru.end(Buffer.from([1, 2, 3, 4, 5]))
		expect(await getBufferFrom(thru, 2)).toEqual(Buffer.from([1, 2]))
		expect(await getBufferFrom(thru, 2)).toEqual(Buffer.from([3, 4]))
		expect(await getBufferFrom(thru, 1)).toEqual(Buffer.from([5]))
		expect(await getBufferFrom(thru, 1, true)).toBeNull()
	})

	it('joins data that arrives across multiple chunks', async () => {
		const readTask = getBufferFrom(thru, 5)
		thru.write(Buffer.from([1, 2]))
		await delay(1)
		thru.write(Buffer.from([3]))
		await delay(1)
		thru.write(Buffer.from([4, 5, 6]))
		expect(await readTask).toEqual(Buffer.from([1, 2, 3, 4, 5]))
		expect(await getBufferFrom(thru, 1)).toEqual(Buffer.from([6]))
	})

	it('throws when the stream ends prematurely', async () => {
		thru.end(Buffer.from([1, 2, 3]))
		await expect(getBufferFrom(thru, 5)).rejects.toThrow(/Stream terminated/)
	})

	it('returns a partial buffer when allowEndOfStream is true', async () => {
		thru.end(Buffer.from([1, 2, 3]))
		expect(await getBufferFrom(thru, 5, true)).toEqual(Buffer.from([1, 2, 3]))
	})

	it('returns null at EOF when allowEndOfStream is true', async () => {
		thru.end()
		expect(await getBufferFrom(thru, 5, true)).toBeNull()
	})

	it('returns a partial buffer assembled from multiple chunks when the stream ends', async () => {
		const readTask = getBufferFrom(thru, 10, true)
		thru.write(Buffer.from([1, 2]))
		await delay(1)
		thru.write(Buffer.from([3, 4]))
		await delay(1)
		thru.end()
		expect(await readTask).toEqual(Buffer.from([1, 2, 3, 4]))
	})

	it('propagates errors', async () => {
		const error = new Error('Mock error')
		const readTask = getBufferFrom(thru, 5)
		thru.destroy(error)
		await expect(readTask).rejects.toThrow(error)
	})

	it('bails on cancellation', async () => {
		const cts = CancellationToken.create()
		const readTask = getBufferFrom(thru, 5, false, cts.token)
		cts.cancel()
		await expect(readTask).rejects.toThrow()
		expect(thru.listenerCount('readable')).toEqual(0)
	})

	it('does not disturb the flowing state of the stream', async () => {
		thru.write(Buffer.from([1, 2, 3]))
		expect(await getBufferFrom(thru, 3)).toEqual(Buffer.from([1, 2, 3]))
		await delay(1)
		expect(thru.readableFlowing).toBeNull()
	})

	it('reads a large buffer that exceeds the high water mark', async () => {
		const size = 1024 * 64 * 3
		const readTask = getBufferFrom(thru, size)
		const source = Buffer.alloc(size)
		for (let i = 0; i < size; i++) {
			source[i] = i % 256
		}

		thru.end(source)
		expect(await readTask).toEqual(source)
	})

	it('works against a Readable with an async _read implementation', async () => {
		let next = 0
		const lazy = new Readable({
			async read() {
				await delay(1)
				this.push(next < 5 ? Buffer.from([next++]) : null)
			},
		})

		expect(await getBufferFrom(lazy, 3)).toEqual(Buffer.from([0, 1, 2]))
		expect(await getBufferFrom(lazy, 3, true)).toEqual(Buffer.from([3, 4]))
	})
})
