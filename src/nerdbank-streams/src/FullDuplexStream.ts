import { create } from "domain";
import { Duplex } from "stream";

export class FullDuplexStream {
    public static CreateStreams(): ITuple<Duplex, Duplex> {
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
            Item1: duplex1,
            Item2: duplex2,
        };
    }
}

export interface ITuple<T1, T2> {
    Item1: T1;
    Item2: T2;
}
