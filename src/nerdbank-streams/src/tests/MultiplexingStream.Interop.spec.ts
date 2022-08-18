import { ChildProcess, spawn } from "child_process";
import "jasmine";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { MultiplexingStream } from "../MultiplexingStream";
import { ChannelOptions } from "../ChannelOptions";
import * as assert from "assert";

[1, 2, 3].forEach(protocolMajorVersion => {
    describe(`MultiplexingStream v${protocolMajorVersion} (interop) `, () => {
        const projectPath = `${__dirname}/../../../../test/Nerdbank.Streams.Interop.Tests`;
        let mx: MultiplexingStream;
        let proc: ChildProcess | null;
        let procExited: Deferred<any>;
        const dotnetEnvBlock: NodeJS.ProcessEnv = {
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1", // prevent warnings in stdout that corrupt our interop stream.
        };
        let expectedError: boolean;

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
                    proc.stdout!.pipe(process.stdout);
                    proc.stderr!.pipe(process.stderr);
                    let buildExitVal = await procExited.promise;
                    console.log(`Build exited with code ${buildExitVal}`);
                    expect(buildExitVal).toEqual(0);
                } catch(error) {
                    let errorMessage = String(error);
                    if (error instanceof Error) {
                        errorMessage = (error as Error).message;
                    }
                    console.log(`Before all failed due to error ${errorMessage}`);
                } finally {
                    proc.kill();
                    proc = null;
                }
            },
            2000000); // leave time for package restore and build

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
                console.log(`Going to create multiplexing stream`);
                mx = await MultiplexingStream.CreateAsync(FullDuplexStream.Splice(proc.stdout!, proc.stdin!), { protocolMajorVersion, seededChannels });
                console.log(`Finished creating multiplexing stream`);
            } catch(error) {
                let errorMessage = String(error);
                if (error instanceof Error) {
                    errorMessage = (error as Error).message;
                }
                console.log(`Before each failed due to error ${errorMessage}`);
                proc.kill();
                proc = null;
                throw error;
            } 
            expectedError = false;
        }, 10000000); // leave time for dotnet to start.

        afterEach(async () => {
            if (mx) {
                mx.dispose();
                try {
                    await mx.completion;
                } catch(error) {
                    if (!expectedError) {
                        throw error;
                    }
                }
                
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

        it("Can accept channel", async () => {
            const channel = await mx.acceptChannelAsync("serverOffer");
            const recv = await readLineAsync(channel.stream);
            await writeAsync(channel.stream, `recv: ${recv}`);
        });

        it("Exchange lots of data", async () => {
            const channel = await mx.offerChannelAsync("clientOffer", { channelReceivingWindowSize: 16 });
            const bigdata = 'ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEF\n';
            await writeAsync(channel.stream, bigdata);
            const recv = await readLineAsync(channel.stream);
            expect(recv).toEqual(`recv: ${bigdata}`);
        });

        it("Can send error to remote", async() => {
            expectedError = true;
            const errorWriteChannel = await mx.offerChannelAsync("clientErrorOffer");
            const responseReceiveChannel = await mx.offerChannelAsync("clientResponseOffer");

            const errorMessage = "couldn't send all of the data";
            const errorToSend = new Error(errorMessage);

            let caughtCompletionErr = false;
            let caughtAcceptanceErr = false;

            errorWriteChannel.completion.catch(err => {
                console.log(`Caught completion rejection ${err}`);
                caughtCompletionErr = true;
            });

            errorWriteChannel.acceptance.catch(err => {
                caughtAcceptanceErr = true;
                console.log(`Caught acceptance rejection ${err}`);
            });

            try {
                await errorWriteChannel.dispose(errorToSend); 
            } catch(error) {
                console.log(`Caught error during dispose call ${error}`);
            }
            
            assert.deepStrictEqual(caughtAcceptanceErr, false);
            assert.deepStrictEqual(caughtCompletionErr, true);

            let expectedMessage = `received error: Remote party indicated writing error: ${errorMessage}`;
            if (protocolMajorVersion == 1) {
                expectedMessage = "didn't receive any errors";
            }

            const receivedMessage = await readLineAsync(responseReceiveChannel.stream);
            assert.deepStrictEqual(receivedMessage?.trim(), expectedMessage);

            console.log("Reached end of error sending test");
        }, 100000000)

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
                readable.once("readable", bytesAvailable.resolve.bind(bytesAvailable));
                readable.once("end", streamEnded.resolve.bind(streamEnded));
                await Promise.race([bytesAvailable.promise, streamEnded.promise]);
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
