import { ChildProcess, spawn } from "child_process";
import "jasmine";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { MultiplexingStream } from "../MultiplexingStream";
import { getBufferFrom } from "../Utilities";

describe("MultiplexingStream (interop)", () => {
    const projectPath = `${__dirname}/../../../Nerdbank.Streams.Interop.Tests`;
    let mx: MultiplexingStream;
    let proc: ChildProcess;
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
                // proc.stdout.pipe(process.stdout);
                proc.stderr.pipe(process.stderr);
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
            ["run", "--no-build", "--project", projectPath],
            dotnetEnvBlock);
        try {
            procExited = new Deferred<any>();
            proc.once("error", (err) => procExited.resolve(err));
            proc.once("exit", (code) => procExited.resolve(code));
            proc.stderr.pipe(process.stderr);
            mx = await MultiplexingStream.CreateAsync(FullDuplexStream.Splice(proc.stdout, proc.stdin));
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
            // console.log(`.NET process exited with: ${exitCode}`);
        }
    }, 10000);

    it("Can offer channel", async () => {
        const channel = await mx.offerChannelAsync("clientOffer");
        await writeAsync(channel.stream, "theclient\n");
        const recv = await readAsync(channel.stream);
        expect(recv).toEqual("recv: theclient\n");
    });

    it("Can accept channel", async () => {
        const channel = await mx.acceptChannelAsync("serverOffer");
        const recv = await readAsync(channel.stream);
        await writeAsync(channel.stream, `recv: ${recv}`);
    });

    function writeAsync(stream: NodeJS.WritableStream, text: string): Promise<void> {
        const deferred = new Deferred<void>();
        stream.write(text, "utf8", deferred.resolve.bind(deferred));
        return deferred.promise;
    }

    async function readAsync(readable: NodeJS.ReadableStream): Promise<string> {
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

        return readBuffer.toString("utf8");
    }
});
