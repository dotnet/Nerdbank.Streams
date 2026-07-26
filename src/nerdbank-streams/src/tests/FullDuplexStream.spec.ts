import { Duplex, PassThrough, Readable, Writable } from 'stream'
import * as rpc from 'vscode-jsonrpc/node'
import { Deferred } from '../Deferred'
import { FullDuplexStream } from '../FullDuplexStream'
import { getBufferFrom } from '../Utilities'
import { delay } from './Timeout'

describe('FullDuplexStream.CreatePair', () => {
	it('should create', () => {
		const pair = FullDuplexStream.CreatePair()
		expect(pair.first).toBeDefined()
		expect(pair.second).toBeDefined()
	})

	it('stream1.write should pass to stream2.read', async () => {
		const pair = FullDuplexStream.CreatePair()
		await writePropagation(pair.first, pair.second)
		await writePropagation(pair.second, pair.first)
	})

	it('stream1 write end leads to stream2 end event', async () => {
		const pair = FullDuplexStream.CreatePair()
		await endPropagatesEndEvent(pair.first, pair.second)
		await endPropagatesEndEvent(pair.second, pair.first)
	})

	it('stream1 write end leads to stream1 finish event', async () => {
		const pair = FullDuplexStream.CreatePair()
		await endRaisesFinishEvent(pair.first)
		await endRaisesFinishEvent(pair.second)
	})

	async function writePropagation(first: Writable, second: Readable): Promise<void> {
		first.write('abc')
		expect(second.read()).toEqual(Buffer.from('abc'))
	}

	async function endRaisesFinishEvent(first: Writable): Promise<void> {
		const signal = new Deferred<void>()
		first.once('finish', () => {
			signal.resolve()
		})
		expect(signal.isCompleted).toBe(false)
		first.end()
		await signal.promise
	}

	async function endPropagatesEndEvent(first: Writable, second: Readable): Promise<void> {
		const signal = new Deferred<void>()
		second.once('end', () => {
			signal.resolve()
		})
		expect(signal.isCompleted).toBe(false)
		first.end()
		second.resume()
		await signal.promise
	}
})

describe('FullDuplexStream.Splice', () => {
	let readable: PassThrough
	let writable: PassThrough
	let duplex: Duplex

	beforeEach(() => {
		readable = new PassThrough({ writableHighWaterMark: 8 })
		writable = new PassThrough({ writableHighWaterMark: 8 })
		duplex = FullDuplexStream.Splice(readable, writable)
	})

	it('Should read from readable', async () => {
		readable.end('hi')
		const buffer = await getBufferFrom(duplex, 2)
		expect(buffer).toEqual(Buffer.from('hi'))
	})

	it('Should write to writable', async () => {
		duplex.write('abc')
		const buffer = await getBufferFrom(writable, 3)
		expect(buffer).toEqual(Buffer.from('abc'))
	})

	it('Terminating writing', async () => {
		duplex.end('the end')
		let buffer: Buffer | null = await getBufferFrom(writable, 7)
		expect(buffer).toEqual(Buffer.from('the end'))
		buffer = await getBufferFrom(writable, 1, true)
		expect(buffer).toBeNull()
	})

	it('Read should yield when data is not ready', async () => {
		const task = writeToStream(duplex, 'abcdefgh', 4)
		const buffer = await getBufferFrom(writable, 32)
		await task
		expect(buffer.length).toEqual(32)
	})

	it('unshift', async () => {
		duplex.unshift(Buffer.from([1, 2, 3]))
		expect(duplex.read()).toEqual(Buffer.from([1, 2, 3]))
	})

	it('unshift what was already read', async () => {
		readable.write(Buffer.from([1, 2, 3, 4]))
		const first = await getBufferFrom(duplex, 4)
		expect(first).toEqual(Buffer.from([1, 2, 3, 4]))
		duplex.unshift(first)
		expect(await getBufferFrom(duplex, 4)).toEqual(Buffer.from([1, 2, 3, 4]))
	})

	it('reads synchronously data that is already available', () => {
		readable.write('abc')
		expect(duplex.read()).toEqual(Buffer.from('abc'))
	})

	it('supports the data event', async () => {
		const received = new Deferred<Buffer>()
		duplex.on('data', chunk => received.resolve(chunk))
		readable.write('abc')
		expect(await received.promise).toEqual(Buffer.from('abc'))
	})

	it('supports pipe', async () => {
		const destination = new PassThrough()
		duplex.pipe(destination)
		readable.write('abc')
		expect(await getBufferFrom(destination, 3)).toEqual(Buffer.from('abc'))
	})

	it('supports async iteration', async () => {
		readable.end('abcdef')
		const chunks: Buffer[] = []
		for await (const chunk of duplex) {
			chunks.push(chunk as Buffer)
		}

		expect(Buffer.concat(chunks)).toEqual(Buffer.from('abcdef'))
	})

	it('propagates the end of the readable stream', async () => {
		readable.end('abc')
		expect(await getBufferFrom(duplex, 3)).toEqual(Buffer.from('abc'))
		expect(await getBufferFrom(duplex, 1, true)).toBeNull()
		expect(duplex.readableEnded).toBe(true)
	})

	it('raises the end event', async () => {
		const ended = new Deferred<void>()
		duplex.once('end', () => ended.resolve())
		readable.end()
		duplex.resume()
		await ended.promise
	})

	it('propagates errors from the readable stream', async () => {
		const error = new Error('Mock error')
		const raised = new Deferred<Error>()
		duplex.once('error', err => raised.resolve(err))
		readable.destroy(error)
		expect(await raised.promise).toEqual(error)
	})

	it('propagates errors from the writable stream', async () => {
		const error = new Error('Mock error')
		const raised = new Deferred<Error>()
		duplex.once('error', err => raised.resolve(err))
		writable.destroy(error)
		expect(await raised.promise).toEqual(error)
	})

	it('faults when the readable stream is destroyed without ending', async () => {
		const raised = new Deferred<Error>()
		duplex.once('error', err => raised.resolve(err))
		readable.destroy()
		expect(await raised.promise).toBeInstanceOf(Error)
	})

	it('does not leak listeners on the readable stream once it ends', async () => {
		readable.end('abc')
		expect(await getBufferFrom(duplex, 3)).toEqual(Buffer.from('abc'))
		expect(await getBufferFrom(duplex, 1, true)).toBeNull()
		expect(readable.listenerCount('readable')).toEqual(0)
	})

	it('honors backpressure from its own consumer', async () => {
		const highWaterMark = duplex.readableHighWaterMark
		const firstChunk = Buffer.alloc(highWaterMark * 4, 1)
		const secondChunk = Buffer.alloc(highWaterMark * 4, 2)
		readable.write(firstChunk)

		// Prime the pump so the duplex starts pulling from the source.
		expect(await getBufferFrom(duplex, 1)).toEqual(firstChunk.subarray(0, 1))

		readable.write(secondChunk)
		await delay(5)

		// Now that the duplex is saturated, it must not pull anything more from the source.
		expect(readable.readableLength).toEqual(secondChunk.length)

		// All the data must still eventually arrive, in order.
		const rest = await getBufferFrom(duplex, firstChunk.length + secondChunk.length - 1)
		expect(rest).toEqual(Buffer.concat([firstChunk.subarray(1), secondChunk]))
	})

	it('interoperates with vscode-jsonrpc', async () => {
		const pair = FullDuplexStream.CreatePair()
		const server = rpc.createMessageConnection(new rpc.StreamMessageReader(pair.first), new rpc.StreamMessageWriter(pair.first))
		const client = rpc.createMessageConnection(new rpc.StreamMessageReader(pair.second), new rpc.StreamMessageWriter(pair.second))
		try {
			server.onRequest('add', (a: number, b: number) => a + b)
			server.listen()
			client.listen()

			expect(await client.sendRequest('add', 1, 2)).toEqual(3)
			expect(await client.sendRequest('add', 5, 6)).toEqual(11)
		} finally {
			client.dispose()
			server.dispose()
		}
	})

	it('does not drain the source stream before it is read', async () => {
		readable.write('abc')
		await delay(5)
		expect(readable.readableLength).toEqual(3)
		expect(await getBufferFrom(duplex, 3)).toEqual(Buffer.from('abc'))
	})

	async function writeToStream(stream: NodeJS.ReadWriteStream, message: string, repeat: number) {
		while (repeat--) {
			stream.write(message)
			await delay(2)
		}
	}
})
