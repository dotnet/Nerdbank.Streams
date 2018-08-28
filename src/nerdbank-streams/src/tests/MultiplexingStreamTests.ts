import { MultiplexingStream } from '../MultiplexingStream';

import 'jasmine';

describe('MultiplexingStream', () => {

    it('Should resolve completion upon disposal', async () => {
        var stream = await MultiplexingStream.CreateAsync();
        expect(stream.isDisposed).toBe(false);
        stream.Dispose();
        expect(stream.isDisposed).toBe(true);
        await stream.completion;
    });

    it('should work', () => {

    });
});
