import "jasmine";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { MultiplexingStream } from "../MultiplexingStream";
import { getBufferFrom } from "../Utilities";
import { startJsonRpc } from "./jsonRpcStreams";
import { timeout } from "./Timeout";
import { Channel } from "../Channel";
import CancellationToken from "cancellationtoken";
import * as assert from "assert";

[1, 2, 3].forEach(protocolMajorVersion => {
    describe(`MultiplexingStream v${protocolMajorVersion}`, () => {
        let mx1: MultiplexingStream;
        let mx2: MultiplexingStream;
        beforeEach(async () => {
            const underlyingPair = FullDuplexStream.CreatePair();
            const mxs = await Promise.all([
                MultiplexingStream.CreateAsync(underlyingPair.first, { protocolMajorVersion }),
                MultiplexingStream.CreateAsync(underlyingPair.second, { protocolMajorVersion }),
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

        it("rejects null stream", async () => {
            expectThrow(MultiplexingStream.CreateAsync(null!));
            expectThrow(MultiplexingStream.CreateAsync(undefined!));
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

        it("An offered anonymous channel is accepted", async () => {
            const rpcChannels = await Promise.all([
                mx1.offerChannelAsync("test"),
                mx2.acceptChannelAsync("test"),
            ]);

            const offer = mx1.createChannel();

            // Send a few bytes on the anonymous channel.
            offer.stream.write(new Buffer([1, 2, 3]));

            // await until we've confirmed the ID could have propagated
            // to the remote party.
            rpcChannels[0].stream.write(new Buffer(1));
            await getBufferFrom(rpcChannels[1].stream, 1);

            const accept = mx2.acceptChannel(offer.id);

            // Receive the few bytes on the new channel.
            const recvOnChannel = await getBufferFrom(accept.stream, 3);
            expect(recvOnChannel).toEqual(new Buffer([1, 2, 3]));

            // Confirm the original party recognizes acceptance.
            await offer.acceptance;
        });

        it("An offered anonymous channel is rejected", async () => {
            const rpcChannels = await Promise.all([
                mx1.offerChannelAsync("test"),
                mx2.acceptChannelAsync("test"),
            ]);

            const offer = mx1.createChannel();

            // Send a few bytes on the anonymous channel.
            offer.stream.write(new Buffer([1, 2, 3]));

            // await until we've confirmed the ID could have propagated
            // to the remote party.
            rpcChannels[0].stream.write(new Buffer(1));
            await getBufferFrom(rpcChannels[1].stream, 1);

            mx2.rejectChannel(offer.id);

            // Confirm the original party recognizes rejection.
            await expectThrow(offer.acceptance);
        });

        it("Channel offer is canceled by sender", async () => {
            const cts = CancellationToken.create();
            const offer = mx1.offerChannelAsync('', undefined, cts.token);
            cts.cancel();
            await assert.rejects(offer);
        });

        it("Channel offer is rejected by event handler", async () => {
            const handler = new Deferred<void>();
            mx2.on("channelOffered", (args) => {
                try {
                    expect(args.name).toEqual("myname");
                    expect(args.isAccepted).toEqual(false);
                    mx2.rejectChannel(args.id);
                    handler.resolve();
                } catch (error) {
                    handler.reject(error);
                }
            });

            await expectThrow(mx1.offerChannelAsync("myname"));
            await handler.promise; // rethrow any failures in the handler.
        });

        it("Channel offer is accepted by event handler", async () => {
            const handler = new Deferred<Channel>();
            mx2.on("channelOffered", (args) => {
                try {
                    expect(args.name).toEqual("myname");
                    expect(args.isAccepted).toEqual(false);
                    const channel = mx2.acceptChannel(args.id);
                    handler.resolve(channel);
                } catch (error) {
                    handler.reject(error);
                }
            });

            const offer = mx1.offerChannelAsync("myname");
            const offeredChannel = await offer;
            const acceptedChannel = await handler.promise; // rethrow any failures in the handler.
            offeredChannel.stream.end();
            acceptedChannel.stream.end();
        });

        it("Channel offer is observed by event handler as accepted", async () => {
            const handler = new Deferred<void>();
            mx2.on("channelOffered", (args) => {
                try {
                    expect(args.name).toEqual("myname");
                    expect(args.isAccepted).toEqual(true);
                    handler.resolve();
                } catch (error) {
                    handler.reject(error);
                }
            });

            const accept = mx2.acceptChannelAsync("myname");
            const offeredChannel = await mx1.offerChannelAsync("myname");
            await accept;
            await handler.promise; // rethrow any failures in the handler.
            offeredChannel.stream.end();
        });

        it("Can use JSON-RPC over a channel", async () => {
            const rpcChannels = await Promise.all([
                mx1.offerChannelAsync("test"),
                mx2.acceptChannelAsync("test"),
            ]);

            const rpc1 = startJsonRpc(rpcChannels[0]);
            const rpc2 = startJsonRpc(rpcChannels[1]);

            rpc2.onRequest("add", (a: number, b: number) => a + b);
            rpc2.listen();

            rpc1.listen();
            const sum = await rpc1.sendRequest("add", 1, 2);
            expect(sum).toEqual(3);
        });

        it("Can exchange data over channel", async () => {
            const channels = await Promise.all([
                mx1.offerChannelAsync("test"),
                mx2.acceptChannelAsync("test"),
            ]);
            channels[0].stream.write("abc");
            expect(await getBufferFrom(channels[1].stream, 3)).toEqual(new Buffer("abc"));
        });

        it("Can exchange data over two channels", async () => {
            const channels = await Promise.all([
                mx1.offerChannelAsync("test"),
                mx1.offerChannelAsync("test2"),
                mx2.acceptChannelAsync("test"),
                mx2.acceptChannelAsync("test2"),
            ]);
            channels[0].stream.write("abc");
            channels[3].stream.write("def");
            channels[3].stream.write("ghi");
            expect(await getBufferFrom(channels[2].stream, 3)).toEqual(new Buffer("abc"));
            expect(await getBufferFrom(channels[1].stream, 6)).toEqual(new Buffer("defghi"));
        });

        it("end of channel", async () => {
            const channels = await Promise.all([
                mx1.offerChannelAsync("test"),
                mx2.acceptChannelAsync("test"),
            ]);
            channels[0].stream.end("finished");
            expect(await getBufferFrom(channels[1].stream, 8)).toEqual(new Buffer("finished"));
            expect(await getBufferFrom(channels[1].stream, 1, true)).toBeNull();
        });

        it("channel terminated", async () => {
            const channels = await Promise.all([
                mx1.offerChannelAsync("test"),
                mx2.acceptChannelAsync("test"),
            ]);
            channels[0].dispose();
            expect(await getBufferFrom(channels[1].stream, 1, true)).toBeNull();
            await channels[1].completion;
        });

        it("channels complete when mxstream is disposed", async () => {
            const channels = await Promise.all([
                mx1.offerChannelAsync("test"),
                mx2.acceptChannelAsync("test"),
            ]);
            mx1.dispose();

            // Verify that both mxstream's complete when one does.
            await mx1.completion;
            await mx2.completion;

            // Verify that the disposed mxstream completes its own channels.
            await channels[0].completion;

            // Verify that the mxstream that closes because its counterpart closed also completes its own channels.
            await channels[1].completion;
        });

        it("offered channels must have names", async () => {
            await expectThrow(mx1.offerChannelAsync(null!));
            await expectThrow(mx1.offerChannelAsync(undefined!));
        });

        it("offered channel name may be blank", async () => {
            await Promise.all([
                mx1.offerChannelAsync(""),
                mx2.acceptChannelAsync(""),
            ]);
        });

        it("accepted channels must have names", async () => {
            await expectThrow(mx1.acceptChannelAsync(null!));
            await expectThrow(mx1.acceptChannelAsync(undefined!));
        });

        if (protocolMajorVersion < 3) {
            it("Rejects seeded channels", async () => {
                const underlyingPair = FullDuplexStream.CreatePair();
                await expectThrow<MultiplexingStream>(MultiplexingStream.CreateAsync(underlyingPair.first, { protocolMajorVersion, seededChannels: [{}] }));
            });
        }
    });

    async function expectThrow<T>(promise: Promise<T>): Promise<any> {
        try {
            await promise;
            fail("Expected error not thrown.");
        } catch (error) {
            return error;
        }
    }
});
