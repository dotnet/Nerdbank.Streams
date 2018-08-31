import "jasmine";
import { FullDuplexStream } from "../FullDuplexStream";
import { MultiplexingStream } from "../MultiplexingStream";
import { getBufferFrom } from "../Utilities";
import { timeout } from "./Timeout";

describe("MultiplexingStream", () => {
    let mx1: MultiplexingStream;
    let mx2: MultiplexingStream;
    beforeEach(async () => {
        const underlyingPair = FullDuplexStream.CreatePair();
        const mxs = await Promise.all([
            MultiplexingStream.CreateAsync(underlyingPair.first),
            MultiplexingStream.CreateAsync(underlyingPair.second),
        ]);
        mx1 = mxs.pop();
        mx2 = mxs.pop();
    });

    afterEach(() => {
        if (mx1) {
            mx1.dispose();
        }

        if (mx2) {
            mx2.dispose();
        }
    });

    it("rejects null stream", async () => {
        expectThrow(MultiplexingStream.CreateAsync(null));
    });

    it("isDisposed set upon disposal", async () => {
        expect(mx1.isDisposed).toBe(false);
        mx1.dispose();
        expect(mx1.isDisposed).toBe(true);
    });

    it("Completion should not complete before disposal", async () => {
        await expectThrow(timeout(mx1.completion, 10));
        mx1.dispose();
        await timeout(mx1.completion, 10);
    });

    it("An offered channel is accepted", async () => {
        await Promise.all([
            mx1.offerChannelAsync("test"),
            mx2.acceptChannelAsync("test"),
        ]);
    });

    it("Can exchange data over channel", async () => {
        const channels = await Promise.all([
            mx1.offerChannelAsync("test"),
            mx2.acceptChannelAsync("test"),
        ]);
        channels[0].duplex.write("abc");
        expect(await getBufferFrom(channels[1].duplex, 3)).toEqual(new Buffer("abc"));
    });

    it("Can exchange data over two channels", async () => {
        const channels = await Promise.all([
            mx1.offerChannelAsync("test"),
            mx1.offerChannelAsync("test2"),
            mx2.acceptChannelAsync("test"),
            mx2.acceptChannelAsync("test2"),
        ]);
        channels[0].duplex.write("abc");
        channels[3].duplex.write("def");
        channels[3].duplex.write("ghi");
        expect(await getBufferFrom(channels[2].duplex, 3)).toEqual(new Buffer("abc"));
        expect(await getBufferFrom(channels[1].duplex, 6)).toEqual(new Buffer("defghi"));
    });

    it("end of channel", async () => {
        const channels = await Promise.all([
            mx1.offerChannelAsync("test"),
            mx2.acceptChannelAsync("test"),
        ]);
        channels[0].duplex.end("finished");
        expect(await getBufferFrom(channels[1].duplex, 8)).toEqual(new Buffer("finished"));
        expect(await getBufferFrom(channels[1].duplex, 1, true)).toEqual(new Buffer(""));
    });

    it("channel terminated", async () => {
        const channels = await Promise.all([
            mx1.offerChannelAsync("test"),
            mx2.acceptChannelAsync("test"),
        ]);
        channels[0].dispose();
        expect(await getBufferFrom(channels[1].duplex, 1, true)).toEqual(new Buffer(""));
        await channels[1].completion;
    });

    it("offered channels must have names", async () => {
        await expectThrow(mx1.offerChannelAsync(null));
    });

    it("accepted channels must have names", async () => {
        await expectThrow(mx1.acceptChannelAsync(null));
    });
});

async function expectThrow<T>(promise: Promise<T>): Promise<T> {
    try {
        await promise;
        fail("Expected error not thrown.");
    } catch {
        return null;
    }
}
