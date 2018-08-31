import { PassThrough } from "stream";
import { FullDuplexStream } from "../FullDuplexStream";
import { getBufferFrom } from "../Utilities";

describe("FullDuplexStream.CreatePair", () => {

    it("should create", () => {
        const pair = FullDuplexStream.CreatePair();
        expect(pair.first).not.toBe(null);
        expect(pair.second).not.toBe(null);
    });

    it("stream1.write should pass to stream2.read", () => {
        const pair = FullDuplexStream.CreatePair();
        pair.first.write("abc");
        expect(pair.second.read()).toEqual(new Buffer("abc"));
    });

    it("stream2.write should pass to stream1.read", () => {
        const pair = FullDuplexStream.CreatePair();
        pair.second.write("abc");
        expect(pair.first.read()).toEqual(new Buffer("abc"));
    });

    it("stream1 write end leads to stream2 read end", async () => {
        const pair = FullDuplexStream.CreatePair();
        pair.first.end();
        expect(pair.first.read()).toBeNull();
    });

    it("stream2 write end leads to stream1 read end", async () => {
        const pair = FullDuplexStream.CreatePair();
        pair.second.end();
        expect(pair.first.read()).toBeNull();
    });
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
        expect(buffer).toEqual(new Buffer("hi"));
    });

    it("Should write to writable", async () => {
        duplex.write("abc");
        const buffer = await getBufferFrom(writable, 3);
        expect(buffer).toEqual(new Buffer("abc"));
    });

    it("Terminating writing", async () => {
        duplex.end("the end");
        let buffer = await getBufferFrom(writable, 7);
        expect(buffer).toEqual(new Buffer("the end"));
        buffer = await getBufferFrom(writable, 1, true);
        expect(buffer).toEqual(new Buffer(""));
    });
});
