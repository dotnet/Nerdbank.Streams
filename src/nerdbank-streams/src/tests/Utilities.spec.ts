import CancellationToken from "cancellationtoken";
import { PassThrough } from "stream"
import { readAsync, sliceStream } from "../Utilities";

let thru: PassThrough
beforeEach(() => {
    thru = new PassThrough();
})

describe('readAsync', function () {
    it('returns immediately with results', async function () {
        thru.write(Buffer.from([1, 2, 3]))
        thru.write(Buffer.from([4, 5, 6]))

        const result = await readAsync(thru)
        expect(result).toEqual(Buffer.from([1, 2, 3, 4, 5, 6]))
    })

    it('to wait for data', async function () {
        const resultPromise = readAsync(thru);

        thru.write(Buffer.from([1, 2, 3]))
        thru.write(Buffer.from([4, 5, 6]))

        const result = await resultPromise;
        expect(result).toEqual(Buffer.from([1, 2, 3]))
    })

    it('to return null at EOF', async function () {
        thru.end()
        expect(await readAsync(thru)).toBeNull()
    })

    it('to propagate errors', async function () {
        const error = new Error('Mock error')
        thru.destroy(error)
        await expectAsync(readAsync(thru)).toBeRejectedWith(error);
    })

    it('bails on cancellation', async function () {
        const cts = CancellationToken.create();
        const readPromise = readAsync(thru, cts.token);
        cts.cancel();
        await expectAsync(readPromise).toBeRejected();
    })
})

describe('sliceStream', function () {
    it('returns null on empty', async function () {
        thru.end()
        const slice = sliceStream(thru, 5)
        expect(slice.read()).toBeNull()
    })

    it('returns subset of upper stream', async function () {
        thru.push(Buffer.from([1, 2, 3, 4, 5, 6]))
        const slice = sliceStream(thru, 3)
        expect(await readAsync(slice)).toEqual(Buffer.from([1, 2, 3]))
        expect(await readAsync(slice)).toBeNull()
        expect(await readAsync(thru)).toEqual(Buffer.from([4, 5, 6]))
    })
})
