import { Duplex, PassThrough } from "stream";
import duplexer = require('plexer')

export class FullDuplexStream {
    public static CreatePair(): { first: Duplex, second: Duplex } {
        const pass1 = new PassThrough();
        const pass2 = new PassThrough();
        return {
            first: FullDuplexStream.Splice(pass1, pass2),
            second: FullDuplexStream.Splice(pass2, pass1),
        };
    }

    public static Splice(readable: NodeJS.ReadableStream, writable: NodeJS.WritableStream): Duplex {
        return duplexer(writable, readable)
    }
}
