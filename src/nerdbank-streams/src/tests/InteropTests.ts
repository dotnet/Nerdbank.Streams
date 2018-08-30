import { ChildProcess, spawn } from "child_process";
import "jasmine";
import { Deferred } from "../Deferred";
import { FullDuplexStream } from "../FullDuplexStream";
import { MultiplexingStream } from "../MultiplexingStream";

describe("MultiplexingStream", () => {
    const projectPath = `${__dirname}/../../../Nerdbank.Streams.Interop.Tests`;
    let mx: MultiplexingStream;
    let proc: ChildProcess;
    let procExited: Deferred<any>;
    beforeAll(
        async () => {
            proc = spawn("dotnet", [
                "build",
                projectPath,
            ]);
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
    beforeEach(async (done) => {
        proc = spawn("dotnet", [
            "run",
            "--no-build",
            "--project",
            projectPath,
        ]);
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
        } finally {
            done();
        }
    }, 10000); // leave time for dotnet to start.

    afterEach(async (done) => {
        try {
            if (mx) {
                mx.dispose();
            }

            if (proc) {
                const exitCode = await procExited.promise;
                // console.log(`.NET process exited with: ${exitCode}`);
            }
        } finally {
            done();
        }
    }, 10000);

    it("Can offer channel", async () => {
        const channel = await mx.offerChannelAsync("clientOffer");
    });

    it("Can accept channel", async () => {
        const channel = await mx.acceptChannelAsync("serverOffer");
    });
});
