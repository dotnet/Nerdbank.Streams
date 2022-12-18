import { PassThrough, Readable, Writable } from "stream";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { getBufferFrom, readAsync } from "../Utilities";
import { delay } from "./Timeout";

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

    it("stream1 write end leads to stream1 finish event", async () => {
        const pair = FullDuplexStream.CreatePair();
        await endRaisesFinishEvent(pair.first);
        await endRaisesFinishEvent(pair.second);
    });

    async function writePropagation(first: Writable, second: Readable): Promise<void> {
        first.write("abc");
        expect(await readAsync(second)).toEqual(Buffer.from("abc"));
    }

    async function endRaisesFinishEvent(first: Writable): Promise<void> {
        const signal = new Deferred<void>();
        first.once("finish", () => {
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
        readable = new PassThrough({ writableHighWaterMark: 8 });
        writable = new PassThrough({ writableHighWaterMark: 8 });
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

    it("unshift", async () => {
        duplex.unshift(Buffer.from([1, 2, 3]))
        const result = duplex.read()
        expect(result).toEqual(Buffer.from([1, 2, 3]))
    })

    it("Read should yield when data is not ready", async () => {
        const task = writeToStream(duplex, "abcdefgh", 4);
        const buffer = await getBufferFrom(writable, 32);
        await task;
        expect(buffer.length).toEqual(32);
    });

    async function writeToStream(stream: NodeJS.ReadWriteStream, message: string, repeat: number) {
        while (repeat--) {
            stream.write(message);
            await delay(2);
        }
    }
});
