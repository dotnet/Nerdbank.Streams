import { Duplex, PassThrough, Readable } from 'stream'

export class FullDuplexStream {
	public static CreatePair(): { first: Duplex; second: Duplex } {
		const pass1 = new PassThrough()
		const pass2 = new PassThrough()
		return {
			first: FullDuplexStream.Splice(pass1, pass2),
			second: FullDuplexStream.Splice(pass2, pass1),
		}
	}

	/**
	 * Creates a full duplex stream from a readable stream and a writable stream.
	 * @param readable The stream that the duplex stream will read from.
	 * @param writable The stream that the duplex stream will write to.
	 * @returns A duplex stream. Ending the duplex stream ends `writable`, but destroying the
	 * duplex stream does not destroy either of the underlying streams.
	 */
	public static Splice(readable: NodeJS.ReadableStream, writable: NodeJS.WritableStream): Duplex {
		let ended = false
		let readableListenerAttached = false

		// Data is pulled from the source stream on demand (rather than pushed eagerly) so that
		// back-pressure is preserved and so that data that is already available on the source stream
		// can be read synchronously from the duplex stream.
		const pump = () => {
			if (ended || duplex.destroyed) {
				return
			}

			let chunk: any
			while ((chunk = readable.read()) !== null) {
				if (!duplex.push(chunk)) {
					// Our own consumer is saturated. `_read` will be called again when it wants more.
					return
				}
			}

			if ((readable as Readable).readableEnded) {
				onEnd()
				return
			}

			if (!readableListenerAttached) {
				readableListenerAttached = true
				readable.once('readable', onReadable)
			}
		}

		const onReadable = () => {
			readableListenerAttached = false
			pump()
		}

		const detachReadableListener = () => {
			if (readableListenerAttached) {
				readableListenerAttached = false
				readable.removeListener('readable', onReadable)
			}
		}

		const onEnd = () => {
			if (!ended) {
				ended = true
				detachReadableListener()
				if (!duplex.destroyed) {
					duplex.push(null)
				}
			}
		}

		const onClose = () => {
			if (!ended) {
				// The source stream was destroyed before it ended, so our own readable side can never complete.
				detachReadableListener()
				duplex.destroy(new Error('Premature close of the underlying readable stream.'))
			}
		}

		const onError = (err: Error) => {
			duplex.destroy(err)
		}

		const duplex = new Duplex({
			read() {
				pump()
			},

			write(chunk, encoding, callback) {
				writable.write(chunk, encoding, callback)
			},

			final(callback) {
				writable.end(callback)
			},

			destroy(error, callback) {
				// Stop pumping, but leave the 'error' handlers attached. If either underlying stream
				// fails later, forwarding the error to this (already destroyed) stream harmlessly
				// absorbs it instead of crashing the process with an unhandled 'error' event.
				detachReadableListener()
				readable.removeListener('end', onEnd)
				readable.removeListener('close', onClose)
				callback(error)
			},
		})

		readable.on('end', onEnd)
		readable.on('close', onClose)
		readable.on('error', onError)
		writable.on('error', onError)

		return duplex
	}
}
