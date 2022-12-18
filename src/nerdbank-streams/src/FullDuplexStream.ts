import { Duplex, PassThrough } from 'stream'

export class FullDuplexStream {
	// eslint-disable-next-line @typescript-eslint/naming-convention
	public static CreatePair(): { first: Duplex; second: Duplex } {
		const pass1 = new PassThrough()
		const pass2 = new PassThrough()
		return {
			first: FullDuplexStream.Splice(pass1, pass2),
			second: FullDuplexStream.Splice(pass2, pass1),
		}
	}

	// eslint-disable-next-line @typescript-eslint/naming-convention
	public static Splice(readable: NodeJS.ReadableStream, writable: NodeJS.WritableStream): Duplex {
		const duplex = new Duplex({
			write(chunk, encoding, callback) {
				writable.write(chunk, encoding, callback)
			},

			final(callback) {
				writable.end(callback)
			},
		})

		// All reads and events come directly from the readable stream.
		duplex.read = readable.read.bind(readable)
		duplex.on = readable.on.bind(readable) as any

		return duplex
	}
}
