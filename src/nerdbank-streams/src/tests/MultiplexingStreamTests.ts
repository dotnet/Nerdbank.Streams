import { MultiplexingStream } from '../MultiplexingStream';
import { timeout } from './Timeout';
import 'jasmine';
import { Duplex } from 'stream';
import { log } from 'util';
import { DuplexPair } from './DuplexPair';
import { Deferred } from '../Deferred';
import { getBufferFrom } from '../Utilities';

describe('MultiplexingStream', () => {
    var mx1: MultiplexingStream;
    var mx2: MultiplexingStream;
    beforeEach(async () => {
        var underlyingPair = DuplexPair.Create();
        var mxs = await Promise.all([
            MultiplexingStream.CreateAsync(underlyingPair.Item1),
            MultiplexingStream.CreateAsync(underlyingPair.Item2)
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

    it('rejects null stream', async () => {
        try {
            await MultiplexingStream.CreateAsync(null);
            fail("expected error not thrown");
        }
        catch { }
    });

    it('isDisposed set upon disposal', async () => {
        expect(mx1.isDisposed).toBe(false);
        mx1.dispose();
        expect(mx1.isDisposed).toBe(true);
    });

    it('Completion should not complete before disposal', async () => {
        try {
            await timeout(mx1.completion, 10);
            fail("completion was already resolved");
        }
        catch { }

        mx1.dispose();
        await timeout(mx1.completion, 10);
    });

    it('An offered channel is accepted', async () => {
        await Promise.all([
            mx1.offerChannelAsync('test'),
            mx2.acceptChannelAsync('test'),
        ]);
    });

    it('Can exchange data over channel', async () => {
        var channels = await Promise.all([
            mx1.offerChannelAsync('test'),
            mx2.acceptChannelAsync('test'),
        ]);
        channels[0].duplex.write('abc');
        expect(await getBufferFrom(channels[1].duplex, 3)).toEqual(new Buffer('abc'));
    });

    it('Can exchange data over two channels', async () => {
        var channels = await Promise.all([
            mx1.offerChannelAsync('test'),
            mx1.offerChannelAsync('test2'),
            mx2.acceptChannelAsync('test'),
            mx2.acceptChannelAsync('test2'),
        ]);
        channels[0].duplex.write('abc');
        channels[3].duplex.write('def');
        channels[3].duplex.write('ghi');
        expect(await getBufferFrom(channels[2].duplex, 3)).toEqual(new Buffer('abc'));
        expect(await getBufferFrom(channels[1].duplex, 6)).toEqual(new Buffer('defghi'));
    });

    it('end of channel', async () => {
        var channels = await Promise.all([
            mx1.offerChannelAsync('test'),
            mx2.acceptChannelAsync('test'),
        ]);
        channels[0].duplex.end('finished');
        expect(await getBufferFrom(channels[1].duplex, 8)).toEqual(new Buffer('finished'));
        expect(await getBufferFrom(channels[1].duplex, 1, true)).toEqual(new Buffer(''));
    });

    it('offered channels must have names', async () => {
        await expectThrow(mx1.offerChannelAsync(null));
    });

    it('accepted channels must have names', async () => {
        await expectThrow(mx1.acceptChannelAsync(null));
    });
});

async function expectThrow<T>(promise: Promise<T>): Promise<T> {
    try {
        await promise;
        fail("Expected error not thrown.");
    }
    catch {
        return null;
    }
}
