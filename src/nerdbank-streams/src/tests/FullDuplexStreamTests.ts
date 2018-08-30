import { FullDuplexStream } from "../FullDuplexStream";

describe("FullDuplexStream", () => {

    it("should create", () => {
        const pair = FullDuplexStream.CreateStreams();
        expect(pair.first).not.toBe(null);
        expect(pair.second).not.toBe(null);
    });

    it("stream1.write should pass to stream2.read", () => {
        const pair = FullDuplexStream.CreateStreams();
        pair.first.write("abc");
        expect(pair.second.read()).toEqual(new Buffer("abc"));
    });

    it("stream2.write should pass to stream1.read", () => {
        const pair = FullDuplexStream.CreateStreams();
        pair.second.write("abc");
        expect(pair.first.read()).toEqual(new Buffer("abc"));
    });

    it("stream1 write end leads to stream2 read end", async () => {
        const pair = FullDuplexStream.CreateStreams();
        pair.first.end();
        expect(pair.first.read()).toBeNull();
    });

    it("stream2 write end leads to stream1 read end", async () => {
        const pair = FullDuplexStream.CreateStreams();
        pair.second.end();
        expect(pair.first.read()).toBeNull();
    });
});
