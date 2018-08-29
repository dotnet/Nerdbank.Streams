import { MultiplexingStream } from '../MultiplexingStream';
import { timeout } from './Timeout';
import 'jasmine';

describe('MultiplexingStream', () => {

    it('isDisposed set upon disposal', async () => {
        var stream = await MultiplexingStream.CreateAsync();
        expect(stream.isDisposed).toBe(false);
        stream.dispose();
        expect(stream.isDisposed).toBe(true);
    });

    it('Completion should not complete before disposal', async () => {
        var stream = await MultiplexingStream.CreateAsync();

        try {
            await timeout(stream.completion, 10);
            throw "completion was already resolved";
        }
        catch { }

        stream.dispose();
        await timeout(stream.completion, 10);
    });
});
