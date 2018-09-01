import { ChildProcess, spawn } from "child_process";
import "jasmine";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { MultiplexingStream } from "../MultiplexingStream";

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
                [ "build", projectPath ],
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
            [ "run", "--no-build", "--project", projectPath ],
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
    });

    it("Can accept channel", async () => {
        const channel = await mx.acceptChannelAsync("serverOffer");
    });
});
