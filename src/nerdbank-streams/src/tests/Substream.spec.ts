import "jasmine";
import { PassThrough } from "stream";
import { Deferred } from "../Deferred";
import { getBufferFrom, readSubstream, writeAsync, writeSubstream } from "../Utilities";

describe("Substream", () => {
    describe("can write", () => {
        it("an empty stream", async () => {
            const thru = new PassThrough();
            const substream = writeSubstream(thru);

            await endAsync(substream);
            await endAsync(thru);

            expect(await readLengthHeader(thru)).toBe(0);
            await expectEndOfStream(thru);
        });

        it("a single chunk", async () => {
            const payload = Buffer.from([1, 2, 3]);

            const thru = new PassThrough();

            const substream = writeSubstream(thru);
            await writeAsync(substream, payload);

            await endAsync(substream);
            await endAsync(thru);

            const dataLength = await readLengthHeader(thru);
            expect(dataLength).toBe(payload.length);

            const readBuffer = await getBufferFrom(thru, dataLength);
            expect(readBuffer).toEqual(payload);

            expect(await readLengthHeader(thru)).toBe(0);
            await expectEndOfStream(thru);
        });

        it("two chunks", async () => {
            const payload1 = Buffer.from([1, 2, 3]);
            const payload2 = Buffer.from([4, 5, 6]);

            const thru = new PassThrough();

            const substream = writeSubstream(thru);
            await writeAsync(substream, payload1);
            await writeAsync(substream, payload2);

            await endAsync(substream);
            await endAsync(thru);

            let dataLength = await readLengthHeader(thru);
            expect(dataLength).toBe(payload1.length);
            let readBuffer = await getBufferFrom(thru, dataLength);
            expect(readBuffer).toEqual(payload1);

            dataLength = await readLengthHeader(thru);
            expect(dataLength).toBe(payload2.length);
            readBuffer = await getBufferFrom(thru, dataLength);
            expect(readBuffer).toEqual(payload2);

            expect(await readLengthHeader(thru)).toBe(0);
            await expectEndOfStream(thru);
        });

        it("two substreams", async () => {
            const payload1 = Buffer.from([1, 2, 3]);
            const payload2 = Buffer.from([4, 5, 6]);

            const thru = new PassThrough();

            let substream = writeSubstream(thru);
            await writeAsync(substream, payload1);
            await endAsync(substream);

            substream = writeSubstream(thru);
            await writeAsync(substream, payload2);
            await endAsync(substream);

            await endAsync(thru);

            let dataLength = await readLengthHeader(thru);
            expect(dataLength).toBe(payload1.length);
            let readBuffer = await getBufferFrom(thru, dataLength);
            expect(readBuffer).toEqual(payload1);
            expect(await readLengthHeader(thru)).toBe(0);

            dataLength = await readLengthHeader(thru);
            expect(dataLength).toBe(payload2.length);
            readBuffer = await getBufferFrom(thru, dataLength);
            expect(readBuffer).toEqual(payload2);
            expect(await readLengthHeader(thru)).toBe(0);

            await expectEndOfStream(thru);
        });
    });

    describe("can read", () => {
        it("an empty stream", async () => {
            const thru = new PassThrough();
            await writeLengthHeader(thru, 0);
            await endAsync(thru);

            const substream = readSubstream(thru);
            await expectEndOfStream(substream);
            await expectEndOfStream(thru);
        });

        it("a single chunk", async () => {
            const thru = new PassThrough();
            const payload = Buffer.from([1, 2, 3]);
            await writeLengthHeader(thru, payload.length);
            await writeAsync(thru, payload);
            await writeLengthHeader(thru, 0);
            await endAsync(thru);

            const substream = readSubstream(thru);
            const readPayload = await getBufferFrom(substream, payload.length);
            expect(readPayload).toEqual(payload);
            await expectEndOfStream(substream);
            await expectEndOfStream(thru);
        });

        it("two chunks", async () => {
            const thru = new PassThrough();
            const payload1 = Buffer.from([1, 2, 3]);
            const payload2 = Buffer.from([4, 5, 6]);

            await writeLengthHeader(thru, payload1.length);
            await writeAsync(thru, payload1);
            await writeLengthHeader(thru, payload2.length);
            await writeAsync(thru, payload2);
            await writeLengthHeader(thru, 0);
            await endAsync(thru);

            const substream = readSubstream(thru);
            let readPayload = await getBufferFrom(substream, payload1.length);
            expect(readPayload).toEqual(payload1);
            readPayload = await getBufferFrom(substream, payload2.length);
            expect(readPayload).toEqual(payload2);

            await expectEndOfStream(substream);
            await expectEndOfStream(thru);
        });

        it("two substreams", async () => {
            const thru = new PassThrough();
            const payload1 = Buffer.from([1, 2, 3]);
            const payload2 = Buffer.from([4, 5, 6]);

            await writeLengthHeader(thru, payload1.length);
            await writeAsync(thru, payload1);
            await writeLengthHeader(thru, 0);
            await writeLengthHeader(thru, payload2.length);
            await writeAsync(thru, payload2);
            await writeLengthHeader(thru, 0);
            await endAsync(thru);

            let substream = readSubstream(thru);
            let readPayload = await getBufferFrom(substream, payload1.length);
            expect(readPayload).toEqual(payload1);
            await expectEndOfStream(substream);

            substream = readSubstream(thru);
            readPayload = await getBufferFrom(substream, payload2.length);
            expect(readPayload).toEqual(payload2);
            await expectEndOfStream(substream);

            await expectEndOfStream(thru);
        });
    });

    async function readLengthHeader(stream: NodeJS.ReadableStream) {
        const readBuffer = await getBufferFrom(stream, 4);
        const dv = new DataView(readBuffer.buffer, readBuffer.byteOffset, readBuffer.length);
        return dv.getUint32(0, false);
    }

    async function writeLengthHeader(stream: NodeJS.WritableStream, length: number) {
        const dv = new DataView(new ArrayBuffer(4));
        dv.setUint32(0, length, false);
        await writeAsync(stream, Buffer.from(dv.buffer, dv.byteOffset, dv.byteLength));
    }

    async function endAsync(stream: NodeJS.WritableStream) {
        const deferred = new Deferred<void>();
        stream.end(() => deferred.resolve());
        return deferred.promise;
    }

    function tick(): Promise<void> {
        const finished = new Deferred<void>();
        process.nextTick(() => finished.resolve());
        return finished.promise;
    }

    async function expectEndOfStream(stream: NodeJS.ReadableStream): Promise<void> {
        const finished = new Deferred<void>();
        stream.once("end", () => finished.resolve());
        while (!finished.isCompleted) {
            expect(stream.read()).toBeNull();
            await tick();
        }
    }
});
