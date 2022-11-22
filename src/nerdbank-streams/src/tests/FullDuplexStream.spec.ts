import { PassThrough, Readable, Writable } from "stream";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { getBufferFrom } from "../Utilities";

describe("FullDuplexStream.CreatePair", () => {

    it("should create", () => {
        const pair = FullDuplexStream.CreatePair();
        expect(pair.first).toBeDefined();
        expect(pair.second).toBeDefined();
    });

    it("stream1.write should pass to stream2.read", async () => {
        const pair = FullDuplexStream.CreatePair();
        await writePropagation(pair.first, pair.second);
        await writePropagation(pair.second, pair.first);
    });

    it("stream1 write end leads to stream2 end event", async () => {
        const pair = FullDuplexStream.CreatePair();
        await endPropagatesEndEvent(pair.first, pair.second);
        await endPropagatesEndEvent(pair.second, pair.first);
    });

    it("stream1 write end leads to stream2 finish event", async () => {
        const pair = FullDuplexStream.CreatePair();
        await endPropagatesFinishEvent(pair.first, pair.second);
        await endPropagatesFinishEvent(pair.second, pair.first);
    });

    async function writePropagation(first: Writable, second: Readable): Promise<void> {
        first.write("abc");
        expect(second.read()).toEqual(Buffer.from("abc"));
    }

    async function endPropagatesFinishEvent(first: Writable, second: Readable): Promise<void> {
        const signal = new Deferred<void>();
        second.once("finish", () => {
            signal.resolve();
        });
        expect(signal.isCompleted).toBe(false);
        first.end();
        await signal.promise;
    }

    async function endPropagatesEndEvent(first: Writable, second: Readable): Promise<void> {
        const signal = new Deferred<void>();
        second.once("end", () => {
            signal.resolve();
        });
        expect(signal.isCompleted).toBe(false);
        first.end();
        second.resume();
        await signal.promise;
    }
});

describe("FullDuplexStream.Splice", () => {
    let readable: PassThrough;
    let writable: PassThrough;
    let duplex: NodeJS.ReadWriteStream;

    beforeEach(() => {
        readable = new PassThrough();
        writable = new PassThrough();
        duplex = FullDuplexStream.Splice(readable, writable);
    });

    it("Should read from readable", async () => {
        readable.end("hi");
        const buffer = await getBufferFrom(duplex, 2);
        expect(buffer).toEqual(Buffer.from("hi"));
    });

    it("Should write to writable", async () => {
        duplex.write("abc");
        const buffer = await getBufferFrom(writable, 3);
        expect(buffer).toEqual(Buffer.from("abc"));
    });

    it("Terminating writing", async () => {
        duplex.end("the end");
        let buffer: Buffer | null = await getBufferFrom(writable, 7);
        expect(buffer).toEqual(Buffer.from("the end"));
        buffer = await getBufferFrom(writable, 1, true);
        expect(buffer).toBeNull();
    });
});
