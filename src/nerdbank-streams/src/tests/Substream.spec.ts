import { PassThrough } from 'stream'
import { Deferred } from '../Deferred'
import { getBufferFrom, readSubstream, writeAsync, writeSubstream } from '../Utilities'

describe('Substream', () => {
	describe('can write', () => {
		it('an empty stream', async () => {
			const thru = new PassThrough()
			const substream = writeSubstream(thru)

			await endAsync(substream)
			await endAsync(thru)

			expect(await readLengthHeader(thru)).toBe(0)
			await expectEndOfStream(thru)
		})

		it('a single chunk', async () => {
			const payload = Buffer.from([1, 2, 3])

			const thru = new PassThrough()

			const substream = writeSubstream(thru)
			await writeAsync(substream, payload)

			await endAsync(substream)
			await endAsync(thru)

			const dataLength = await readLengthHeader(thru)
			expect(dataLength).toBe(payload.length)

			const readBuffer = await getBufferFrom(thru, dataLength)
			expect(readBuffer).toEqual(payload)

			expect(await readLengthHeader(thru)).toBe(0)
			await expectEndOfStream(thru)
		})

		it('two chunks', async () => {
			const payload1 = Buffer.from([1, 2, 3])
			const payload2 = Buffer.from([4, 5, 6])

			const thru = new PassThrough()

			const substream = writeSubstream(thru)
			await writeAsync(substream, payload1)
			await writeAsync(substream, payload2)

			await endAsync(substream)
			await endAsync(thru)

			let dataLength = await readLengthHeader(thru)
			expect(dataLength).toBe(payload1.length)
			let readBuffer = await getBufferFrom(thru, dataLength)
			expect(readBuffer).toEqual(payload1)

			dataLength = await readLengthHeader(thru)
			expect(dataLength).toBe(payload2.length)
			readBuffer = await getBufferFrom(thru, dataLength)
			expect(readBuffer).toEqual(payload2)

			expect(await readLengthHeader(thru)).toBe(0)
			await expectEndOfStream(thru)
		})

		it('two substreams', async () => {
			const payload1 = Buffer.from([1, 2, 3])
			const payload2 = Buffer.from([4, 5, 6])

			const thru = new PassThrough()

			let substream = writeSubstream(thru)
			await writeAsync(substream, payload1)
			await endAsync(substream)

			substream = writeSubstream(thru)
			await writeAsync(substream, payload2)
			await endAsync(substream)

			await endAsync(thru)

			let dataLength = await readLengthHeader(thru)
			expect(dataLength).toBe(payload1.length)
			let readBuffer = await getBufferFrom(thru, dataLength)
			expect(readBuffer).toEqual(payload1)
			expect(await readLengthHeader(thru)).toBe(0)

			dataLength = await readLengthHeader(thru)
			expect(dataLength).toBe(payload2.length)
			readBuffer = await getBufferFrom(thru, dataLength)
			expect(readBuffer).toEqual(payload2)
			expect(await readLengthHeader(thru)).toBe(0)

			await expectEndOfStream(thru)
		})
	})

	describe('can read', () => {
		it('an empty stream', async () => {
			const thru = new PassThrough()
			await writeLengthHeader(thru, 0)
			await endAsync(thru)

			const substream = readSubstream(thru)
			await expectEndOfStream(substream)
			await expectEndOfStream(thru)
		})

		it('a single chunk', async () => {
			const thru = new PassThrough()
			const payload = Buffer.from([1, 2, 3])
			await writeLengthHeader(thru, payload.length)
			await writeAsync(thru, payload)
			await writeLengthHeader(thru, 0)
			await endAsync(thru)

			const substream = readSubstream(thru)
			const readPayload = await getBufferFrom(substream, payload.length)
			expect(readPayload).toEqual(payload)
			await expectEndOfStream(substream)
			await expectEndOfStream(thru)
		})

		it('two chunks', async () => {
			const thru = new PassThrough()
			const payload1 = Buffer.from([1, 2, 3])
			const payload2 = Buffer.from([4, 5, 6])

			await writeLengthHeader(thru, payload1.length)
			await writeAsync(thru, payload1)
			await writeLengthHeader(thru, payload2.length)
			await writeAsync(thru, payload2)
			await writeLengthHeader(thru, 0)
			await endAsync(thru)

			const substream = readSubstream(thru)
			let readPayload = await getBufferFrom(substream, payload1.length)
			expect(readPayload).toEqual(payload1)
			readPayload = await getBufferFrom(substream, payload2.length)
			expect(readPayload).toEqual(payload2)

			await expectEndOfStream(substream)
			await expectEndOfStream(thru)
		})

		it('two substreams', async () => {
			const thru = new PassThrough()
			const payload1 = Buffer.from([1, 2, 3])
			const payload2 = Buffer.from([4, 5, 6])

			await writeLengthHeader(thru, payload1.length)
			await writeAsync(thru, payload1)
			await writeLengthHeader(thru, 0)
			await writeLengthHeader(thru, payload2.length)
			await writeAsync(thru, payload2)
			await writeLengthHeader(thru, 0)
			await endAsync(thru)

			let substream = readSubstream(thru)
			let readPayload = await getBufferFrom(substream, payload1.length)
			expect(readPayload).toEqual(payload1)
			await expectEndOfStream(substream)

			substream = readSubstream(thru)
			readPayload = await getBufferFrom(substream, payload2.length)
			expect(readPayload).toEqual(payload2)
			await expectEndOfStream(substream)

			await expectEndOfStream(thru)
		})
	})

	describe('streaming behavior', () => {
		it('yields partial chunks as they arrive', async () => {
			const thru = new PassThrough()
			await writeLengthHeader(thru, 6)

			const substream = readSubstream(thru)

			// Write only part of the announced chunk and expect to be able to read it
			// without waiting for the rest of the chunk to arrive.
			await writeAsync(thru, Buffer.from([1, 2, 3]))
			expect(await getBufferFrom(substream, 3)).toEqual(Buffer.from([1, 2, 3]))

			await writeAsync(thru, Buffer.from([4, 5, 6]))
			expect(await getBufferFrom(substream, 3)).toEqual(Buffer.from([4, 5, 6]))

			await writeLengthHeader(thru, 0)
			await endAsync(thru)
			await expectEndOfStream(substream)
		})

		it('reads across chunk boundaries', async () => {
			const thru = new PassThrough()
			await writeLengthHeader(thru, 3)
			await writeAsync(thru, Buffer.from([1, 2, 3]))
			await writeLengthHeader(thru, 3)
			await writeAsync(thru, Buffer.from([4, 5, 6]))
			await writeLengthHeader(thru, 0)
			await endAsync(thru)

			const substream = readSubstream(thru)
			expect(await getBufferFrom(substream, 6)).toEqual(Buffer.from([1, 2, 3, 4, 5, 6]))
			await expectEndOfStream(substream)
			await expectEndOfStream(thru)
		})

		it('can be consumed with getBufferFrom allowing end of stream', async () => {
			const thru = new PassThrough()
			await writeLengthHeader(thru, 3)
			await writeAsync(thru, Buffer.from([1, 2, 3]))
			await writeLengthHeader(thru, 3)
			await writeAsync(thru, Buffer.from([4, 5, 6]))
			await writeLengthHeader(thru, 0)
			await endAsync(thru)

			const substream = readSubstream(thru)
			expect(await getBufferFrom(substream, 10, true)).toEqual(Buffer.from([1, 2, 3, 4, 5, 6]))
			await expectEndOfStream(thru)
		})

		it('can be consumed by async iteration', async () => {
			const thru = new PassThrough()
			await writeLengthHeader(thru, 3)
			await writeAsync(thru, Buffer.from([1, 2, 3]))
			await writeLengthHeader(thru, 3)
			await writeAsync(thru, Buffer.from([4, 5, 6]))
			await writeLengthHeader(thru, 0)
			await writeAsync(thru, Buffer.from([9, 9]))
			await endAsync(thru)

			const substream = readSubstream(thru)
			const chunks: Buffer[] = []
			for await (const chunk of substream) {
				chunks.push(chunk as Buffer)
			}

			expect(Buffer.concat(chunks)).toEqual(Buffer.from([1, 2, 3, 4, 5, 6]))

			// Anything that followed the substream must remain available on the underlying stream.
			expect(await getBufferFrom(thru, 2)).toEqual(Buffer.from([9, 9]))
		})

		it('can be consumed by pipe', async () => {
			const thru = new PassThrough()
			await writeLengthHeader(thru, 3)
			await writeAsync(thru, Buffer.from([1, 2, 3]))
			await writeLengthHeader(thru, 0)
			await writeAsync(thru, Buffer.from([9, 9]))
			await endAsync(thru)

			const substream = readSubstream(thru)
			const destination = new PassThrough()
			substream.pipe(destination)
			expect(await getBufferFrom(destination, 3)).toEqual(Buffer.from([1, 2, 3]))
			expect(await getBufferFrom(thru, 2)).toEqual(Buffer.from([9, 9]))
		})

		it('round-trips a large payload written by writeSubstream', async () => {
			const thru = new PassThrough()
			const payload = Buffer.alloc(1024 * 128)
			for (let i = 0; i < payload.length; i++) {
				payload[i] = i % 256
			}

			const writer = writeSubstream(thru)
			const writeTask = (async () => {
				await writeAsync(writer, payload)
				await endAsync(writer)
				await endAsync(thru)
			})()

			const substream = readSubstream(thru)
			const readPayload = await getBufferFrom(substream, payload.length)
			expect(readPayload).toEqual(payload)
			await writeTask
			await expectEndOfStream(substream)
		})

		it('faults when the underlying stream ends mid-substream', async () => {
			const thru = new PassThrough()
			await writeLengthHeader(thru, 6)
			await writeAsync(thru, Buffer.from([1, 2, 3]))
			await endAsync(thru)

			const substream = readSubstream(thru)
			const errorRaised = new Deferred<Error>()
			substream.on('error', err => errorRaised.resolve(err))
			expect(await getBufferFrom(substream, 3)).toEqual(Buffer.from([1, 2, 3]))
			await expect(getBufferFrom(substream, 3)).rejects.toThrow()
			expect(await errorRaised.promise).toBeInstanceOf(Error)
		})

		it('faults when the underlying stream ends before a full header', async () => {
			const thru = new PassThrough()
			await writeAsync(thru, Buffer.from([0, 0]))
			await endAsync(thru)

			const substream = readSubstream(thru)
			substream.on('error', () => {})
			await expect(getBufferFrom(substream, 1)).rejects.toThrow()
		})

		it('stops consuming the underlying stream when destroyed', async () => {
			const thru = new PassThrough()
			const substream = readSubstream(thru)
			substream.on('error', () => {})

			// Start a read that cannot complete yet, then destroy the substream.
			const readTask = getBufferFrom(substream, 1)
			substream.destroy()
			await expect(readTask).rejects.toThrow()

			// Data written after the substream was destroyed must remain in the underlying stream.
			await writeLengthHeader(thru, 3)
			await writeAsync(thru, Buffer.from([1, 2, 3]))
			expect(await getBufferFrom(thru, 7)).toEqual(Buffer.from([0, 0, 0, 3, 1, 2, 3]))
			expect(thru.listenerCount('readable')).toEqual(0)
		})

		it('propagates errors from the underlying stream', async () => {
			const thru = new PassThrough()
			const error = new Error('Mock error')
			const substream = readSubstream(thru)
			substream.on('error', () => {})
			const readTask = getBufferFrom(substream, 1)
			thru.destroy(error)
			await expect(readTask).rejects.toThrow(error)
		})
	})

	async function readLengthHeader(stream: NodeJS.ReadableStream) {
		const readBuffer = await getBufferFrom(stream, 4)
		const dv = new DataView(readBuffer.buffer, readBuffer.byteOffset, readBuffer.length)
		return dv.getUint32(0, false)
	}

	async function writeLengthHeader(stream: NodeJS.WritableStream, length: number) {
		const dv = new DataView(new ArrayBuffer(4))
		dv.setUint32(0, length, false)
		await writeAsync(stream, Buffer.from(dv.buffer, dv.byteOffset, dv.byteLength))
	}

	async function endAsync(stream: NodeJS.WritableStream) {
		const deferred = new Deferred<void>()
		stream.end(() => deferred.resolve())
		return deferred.promise
	}

	function tick(): Promise<void> {
		const finished = new Deferred<void>()
		process.nextTick(() => finished.resolve())
		return finished.promise
	}

	async function expectEndOfStream(stream: NodeJS.ReadableStream): Promise<void> {
		const finished = new Deferred<void>()
		stream.once('end', () => finished.resolve())
		while (!finished.isCompleted) {
			expect(stream.read()).toBeNull()
			await tick()
		}
	}
})
