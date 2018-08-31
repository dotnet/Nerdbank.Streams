import { Duplex } from "stream";

export class FullDuplexStream {
    public static CreatePair(): { first: Duplex, second: Duplex } {
        let duplex2: Duplex;
        const duplex1 = new Duplex({
            write(chunk, encoding, callback) {
                duplex2.push(chunk, encoding);
                callback();
            },

            read(size) {
                // Nothing to do here, since our buddy pushes directly to us.
            },
        });
        duplex2 = new Duplex({
            write(chunk, encoding, callback) {
                duplex1.push(chunk, encoding);
                callback();
            },

            read(size) {
                // Nothing to do here, since our buddy pushes directly to us.
            },
        });

        return {
            first: duplex1,
            second: duplex2,
        };
    }

    public static Splice(readable: NodeJS.ReadableStream, writable: NodeJS.WritableStream): NodeJS.ReadWriteStream {
        const duplex = new Duplex({
            write(chunk, encoding, callback) {
                writable.write(chunk, encoding, callback);
            },

            final(callback) {
                writable.end(callback);
            },
        });

        // All reads and events come directly from the readable stream.
        duplex.read = readable.read.bind(readable);
        duplex.on = readable.on.bind(readable);

        return duplex;
    }
}
