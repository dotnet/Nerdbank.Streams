import { ChildProcess, spawn } from "child_process";
import "jasmine";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { MultiplexingStream } from "../MultiplexingStream";
import { ChannelOptions } from "../ChannelOptions";

[1, 2, 3].forEach(protocolMajorVersion => {
    describe(`MultiplexingStream v${protocolMajorVersion} (interop) `, () => {
        const projectPath = `${__dirname}/../../../../test/Nerdbank.Streams.Interop.Tests`;
        let mx: MultiplexingStream;
        let proc: ChildProcess | null;
        let procExited: Deferred<any>;
        const dotnetEnvBlock: NodeJS.ProcessEnv = {
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1", // prevent warnings in stdout that corrupt our interop stream.
        };
        beforeAll(
            async () => {
                proc = spawn(
                    "dotnet",
                    ["build", projectPath],
                    dotnetEnvBlock);
                try {
                    procExited = new Deferred<any>();
                    proc.once("error", (err) => procExited.resolve(err));
                    proc.once("exit", (code) => procExited.resolve(code));
                    // proc.stdout!.pipe(process.stdout);
                    proc.stderr!.pipe(process.stderr);
                    expect(await procExited.promise).toEqual(0);
                } finally {
                    proc.kill();
                    proc = null;
                }
            },
            20000); // leave time for package restore and build
        beforeEach(async () => {
            proc = spawn(
                "dotnet",
                ["run", "--no-build", "--project", projectPath, "--", protocolMajorVersion.toString()],
                dotnetEnvBlock);
            try {
                procExited = new Deferred<any>();
                proc.once("error", (err) => procExited.resolve(err));
                proc.once("exit", (code) => procExited.resolve(code));
                proc.stderr!.pipe(process.stderr);
                const seededChannels: ChannelOptions[] | undefined = protocolMajorVersion >= 3 ? [{}] : undefined;
                mx = await MultiplexingStream.CreateAsync(FullDuplexStream.Splice(proc.stdout!, proc.stdin!), { protocolMajorVersion, seededChannels });
            } catch (e) {
                proc.kill();
                proc = null;
                throw e;
            }
        }, 10000); // leave time for dotnet to start.

        afterEach(async () => {
            if (mx) {
                mx.dispose();
                await mx.completion;
            }

            if (proc) {
                const exitCode = await procExited.promise;
                expect(exitCode).toEqual(0);
                // console.log(`.NET process exited with: ${exitCode}`);
            }
        }, 10000);

        it("Can offer channel", async () => {
            const channel = await mx.offerChannelAsync("clientOffer");
            await writeAsync(channel.stream, "theclient\n");
            const recv = await readLineAsync(channel.stream);
            expect(recv).toEqual("recv: theclient\n");
        });

        it("Can offer error completed channel", async () => {
            const errorMsg : string = "Hello world";
            const error : Error = new Error(errorMsg);

            const channelToCompleteWithError = await mx.offerChannelAsync("clientErrorOffer");
            const communicationChannel = await mx.offerChannelAsync("clientErrorOfferComm");

            channelToCompleteWithError.dispose(error);
            channelToCompleteWithError.completion.then(_ => {
                throw new Error("Channel disposed with error didn't complete with error");
            }).catch( (channelCompleteErr) => {
                expect(channelCompleteErr.message).toContain(errorMsg);
            });

            let expectedErrMessage = `Received error from remote side: ${errorMsg}`;
            if(protocolMajorVersion <= 1) {
                expectedErrMessage = "Completed with no error";
            }

            const response = await readLineAsync(communicationChannel.stream);
            expect(response).toContain(expectedErrMessage);
        })

        it("Can accept channel", async () => {
            const channel = await mx.acceptChannelAsync("serverOffer");
            const recv = await readLineAsync(channel.stream);
            await writeAsync(channel.stream, `recv: ${recv}`);
        });

        it("Can accept error completed channel", async () => {
            const channelCompletedWithError = await mx.acceptChannelAsync("serverErrorOffer");
            const errorExpectedMessage : string = "Received error from remote side: Hello World";
            const channelCompleted = new Deferred<void>();

            channelCompletedWithError.completion.then(async _ => {
                expect(protocolMajorVersion).toEqual(1);
                channelCompleted.resolve();
            }).catch(async error => {
                expect(error.message).toContain(errorExpectedMessage);
                channelCompleted.resolve();
            })

            await channelCompleted.promise;
        })

        it("Exchange lots of data", async () => {
            const channel = await mx.offerChannelAsync("clientOffer", { channelReceivingWindowSize: 16 });
            const bigdata = 'ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEF\n';
            await writeAsync(channel.stream, bigdata);
            const recv = await readLineAsync(channel.stream);
            expect(recv).toEqual(`recv: ${bigdata}`);
        });

        if (protocolMajorVersion >= 3) {
            it("Can communicate over seeded channel", async () => {
                const channel = mx.acceptChannel(0);
                await writeAsync(channel.stream, "theclient\n");
                const recv = await readLineAsync(channel.stream);
                expect(recv).toEqual("recv: theclient\n");
            });
        }

        function writeAsync(stream: NodeJS.WritableStream, text: string): Promise<void> {
            const deferred = new Deferred<void>();
            stream.write(text, "utf8", (err: Error | null | undefined) => { if (err) { deferred.reject(err); } else { deferred.resolve(); } });
            return deferred.promise;
        }

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

        async function readLineAsync(readable: NodeJS.ReadableStream): Promise<string | null> {
            const buffers: Buffer[] = [];

            while (true) {
                const segment = await readAsync(readable);
                if (segment === null) {
                    break;
                }

                const lineFeedPosition = segment.indexOf('\n', 0, 'utf8');
                if (lineFeedPosition >= 0) {
                    // Put anything beyond the linefeed back for reading later.
                    readable.unshift(segment.slice(lineFeedPosition + 1));
                    buffers.push(segment.slice(0, lineFeedPosition + 1));
                    break;
                }

                buffers.push(segment);
            }

            if (buffers.length === 0) {
                return null;
            }

            return Buffer.concat(buffers).toString('utf8');
        }
    });
});
