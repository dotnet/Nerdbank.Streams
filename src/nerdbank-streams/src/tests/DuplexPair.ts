import { create } from "domain";
import { Duplex } from "stream";

export class DuplexPair {
    public static Create(): Tuple<Duplex, Duplex> {
        var duplex2: Duplex;
        var duplex1 = new Duplex({
            write(chunk, encoding, callback) {
                duplex2.push(chunk, encoding);
                callback();
            },

            read(size) {
                // Nothing to do here, since our buddy pushes directly to us.
            }
        });
        duplex2 = new Duplex({
            write(chunk, encoding, callback) {
                duplex1.push(chunk, encoding);
                callback();
            },

            read(size) {
                // Nothing to do here, since our buddy pushes directly to us.
            }
        });

        return {
            Item1: duplex1,
            Item2: duplex2,
        };
    }
}

interface Tuple<T1, T2> {
    Item1: T1;
    Item2: T2;
}
