import { Deferred } from './Deferred';
import { CancellationToken } from 'vscode-jsonrpc'
import './MultiplexingStreamOptions';
import { ChannelOptions } from './ChannelOptions';
import { Channel, ChannelClass } from './Channel';
import { MultiplexingStreamOptions } from './MultiplexingStreamOptions';
import { IDisposableObservable } from './IDisposableObservable';
import { randomBytes } from 'crypto';

export class MultiplexingStream implements IDisposableObservable {
    private static readonly protocolMagicNumber = new Buffer([0x2f, 0xdf, 0x1d, 0x50]);
    private readonly _completionSource = new Deferred<void>();

    private _isDisposed: boolean = false;

    private constructor(private stream: NodeJS.ReadWriteStream, private isOdd: boolean, private options: MultiplexingStreamOptions) {
    }

    /**
     * Gets a promise that is resolved or rejected based on how this stream is disposed or fails.
     */
    get completion(): Promise<void> {
        return this._completionSource.promise;
    }

    /**
     * Gets a value indicating whether this instance has been disposed.
     */
    get isDisposed(): boolean {
        return this._isDisposed;
    }

    /**
     * Initializes a new instance of the `MultiplexingStream` class.
     * @param stream The duplex stream to read and write to.
     * @param options Options to customize the behavior of the stream.
     * @param cancellationToken A token whose cancellation aborts the handshake with the remote end.
     * @returns The multiplexing stream, once the handshake is complete.
     */
    public static async CreateAsync(stream: NodeJS.ReadWriteStream, options?: MultiplexingStreamOptions, cancellationToken?: CancellationToken): Promise<MultiplexingStream> {
        if (!stream) {
            throw new Error("stream must be specified.");
        }

        options = options || new MultiplexingStreamOptions();

        // Send the protocol magic number, and a random 16-byte number to establish even/odd assignments.
        var randomSendBuffer = randomBytes(16);
        var sendBuffer = Buffer.concat([MultiplexingStream.protocolMagicNumber, randomSendBuffer]);
        stream.write(sendBuffer);

        var recvBuffer = <Buffer>await this.GetBufferOf(stream, sendBuffer.length);

        for (let i = 0; i < MultiplexingStream.protocolMagicNumber.length; i++) {
            const expected = MultiplexingStream.protocolMagicNumber[i];
            const actual = recvBuffer.readUInt8(i);
            if (expected != actual) {
                throw new Error('Protocol magic number mismatch.');
            }
        }

        var isOdd: boolean;
        for (let i = 0; i < randomSendBuffer.length; i++) {
            const sent = randomSendBuffer[i];
            const recv = recvBuffer.readUInt8(MultiplexingStream.protocolMagicNumber.length + i);
            if (sent > recv) {
                isOdd = true;
                break;
            }
            else if (sent < recv) {
                isOdd = false;
                break;
            }
        }

        if (isOdd === undefined) {
            throw new Error("Unable to determine even/odd party.");
        }

        return new MultiplexingStream(stream, isOdd, options);
    };

    /**
     * Creates an anonymous channel that may be accepted by <see cref="AcceptChannel(int, ChannelOptions)"/>.
     * Its existance must be communicated by other means (typically another, existing channel) to encourage acceptance.
     * @param options A set of options that describe local treatment of this channel.
     * @returns The anonymous channel.
     * @description Note that while the channel is created immediately, any local write to that channel will be buffered locally
     * until the remote party accepts the channel.
     */
    public createChannel(options?: ChannelOptions): Channel {
        return null;
    }

    /**
     * Accepts a channel with a specific ID.
     * @param id The id of the channel to accept.
     * @param options A set of options that describe local treatment of this channel.
     * @description This method can be used to accept anonymous channels created with <see cref="CreateChannel"/>.
     * Unlike <see cref="AcceptChannelAsync(string, ChannelOptions, CancellationToken)"/> which will await
     * for a channel offer if a matching one has not been made yet, this method only accepts an offer
     * for a channel that has already been made.
     */
    public acceptChannel(id: number, options?: ChannelOptions): Channel {
        return null;
    }

    /**
     * Rejects an offer for the channel with a specified ID.
     * @param id The ID of the channel whose offer should be rejected.
     */
    public rejectChannel(id: number) {
    }

    /**
     * Offers a new, named channel to the remote party so they may accept it with <see cref="AcceptChannelAsync(string, ChannelOptions, CancellationToken)"/>.
     * @param name A name for the channel, which must be accepted on the remote end to complete creation.
     * It need not be unique, and may be empty but must not be null.
     * Any characters are allowed, and max length is determined by the maximum frame payload (based on UTF-8 encoding).
     * @param options A set of options that describe local treatment of this channel.
     * @param cancellationToken A cancellation token.
     * @returns A task that completes with the `Channel` if the offer is accepted on the remote end
     * or faults with `MultiplexingProtocolException` if the remote end rejects the channel.
     */
    public async offerChannelAsync(name: string, options?: ChannelOptions, cancellationToken?: CancellationToken): Promise<Channel> {
        return new ChannelClass();
    }

    /**
     * Accepts a channel that the remote end has attempted or may attempt to create.
     * @param name The name of the channel to accept.
     * @param options A set of options that describe local treatment of this channel.
     * @param cancellationToken A token to indicate lost interest in accepting the channel.
     * @returns The `Channel`, after its offer has been received from the remote party and accepted.
     * @description If multiple offers exist with the specified `name`, the first one received will be accepted.
     */
    public async acceptChannelAsync(name: string, options?: ChannelOptions, cancellationToken?: CancellationToken): Promise<Channel> {
        return new ChannelClass();
    }

    /**
     * Disposes the stream.
     */
    public dispose() {
        this._isDisposed = true;
        this._completionSource.resolve();
    };

    private static async GetBufferOf(readable: NodeJS.ReadableStream, size: number): Promise<string | Buffer> {
        while (size > 0) {
            var readBuffer = readable.read(size);
            if (readBuffer === null) {
                var bytesAvailable = new Deferred<void>();
                readable.once('readable', bytesAvailable.resolve);
                await bytesAvailable.promise;
                continue;
            }

            if (readBuffer.length < size) {
                throw new Error("Stream terminated before required bytes were read.");
            }

            return readBuffer;
        }
    }
}
