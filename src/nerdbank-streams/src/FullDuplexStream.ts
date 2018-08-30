import { Duplex } from "stream";

export class FullDuplexStream {
    public static CreateStreams(): { first: Duplex, second: Duplex } {
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
}
