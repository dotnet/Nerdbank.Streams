import "jasmine";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { MultiplexingStream } from "../MultiplexingStream";
import { getBufferFrom } from "../Utilities";
import { ChannelOptions } from "../ChannelOptions";

[3].forEach(protocolMajorVersion => {
    describe(`MultiplexingStream v${protocolMajorVersion} seeded channels`, () => {
        let mx1: MultiplexingStream;
        let mx2: MultiplexingStream;
        const seededChannels: ChannelOptions[] = [{}, {}, {}]
        beforeEach(async () => {
            const underlyingPair = FullDuplexStream.CreatePair();
            const mxs = await Promise.all([
                MultiplexingStream.CreateAsync(underlyingPair.first, { protocolMajorVersion, seededChannels }),
                MultiplexingStream.CreateAsync(underlyingPair.second, { protocolMajorVersion, seededChannels }),
            ]);
            mx1 = mxs.pop()!;
            mx2 = mxs.pop()!;
        });

        afterEach(async () => {
            if (mx1) {
                mx1.dispose();
                await mx1.completion;
            }

            if (mx2) {
                mx2.dispose();
                await mx2.completion;
            }
        });

        it('can send content', async () => {
            const ch1_0 = mx1.acceptChannel(0);
            const ch1_1 = mx1.acceptChannel(1);
            const ch2_0 = mx2.acceptChannel(0);
            const ch2_1 = mx2.acceptChannel(1);

            ch1_0.stream.write("abc");
            expect(await getBufferFrom(ch2_0.stream, 3)).toEqual(Buffer.from("abc"));

            ch2_1.stream.write("abc");
            expect(await getBufferFrom(ch1_1.stream, 3)).toEqual(Buffer.from("abc"));
        });

        it('can be closed', async () => {
            const ch1 = mx1.acceptChannel(0);
            ch1.dispose();

            const ch2 = mx2.acceptChannel(0);
            const readBuffer = await readAsync(ch2.stream);
            expect(readBuffer).toBeNull();
            ch2.dispose();

            await Promise.all([ch1.completion, ch2.completion]);
        }, 120000);

        it('cannot be rejected', () => {
            expect(() => mx1.rejectChannel(0)).toThrow();
        });

        it('cannot be accepted twice', () => {
            mx1.acceptChannel(0);
            expect(() => mx1.acceptChannel(0)).toThrow();
        });

        it('does not overlap channel IDs', () => {
            const channel = mx1.createChannel();
            expect(channel.qualifiedId.id >= seededChannels.length).toBeTruthy();
        });
    });
});

async function readAsync(readable: NodeJS.ReadableStream): Promise<Buffer | null> {
    let readBuffer = readable.read() as Buffer;

    if (readBuffer === null) {
        const bytesAvailable = new Deferred<void>();
        const streamEnded = new Deferred<void>();
        const bytesAvailableCallback = bytesAvailable.resolve.bind(bytesAvailable);
        const streamEndedCallback = streamEnded.resolve.bind(streamEnded);
        readable.once("readable", bytesAvailableCallback);
        readable.once("end", streamEndedCallback);
        await Promise.race([bytesAvailable.promise, streamEnded.promise]);
        readable.removeListener("readable", bytesAvailableCallback);
        readable.removeListener("end", streamEndedCallback);
        if (bytesAvailable.isCompleted) {
            readBuffer = readable.read() as Buffer;
        } else {
            return null;
        }
    }

    return readBuffer;
}
