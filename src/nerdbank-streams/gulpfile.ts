import * as fs from "fs";
import * as gulp from "gulp";
import * as mkdirp from "mkdirp";
import * as nbgv from "nerdbank-gitversioning";
import * as path from "path";
import { promisify } from "util";
import * as ap from "./asyncprocess";

const outDir = "dist";

async function tsc() {
    await ap.execAsync(`node ./node_modules/typescript/bin/tsc -p tsconfig.json`, { cwd: __dirname });
}

async function copyPackageContents() {
    await gulp
        .src([
            "package.json",
            "README.md",
        ])
        .pipe(gulp.dest(outDir));
        await gulp
        .src(["out/*"])
        .pipe(gulp.dest(path.join(outDir, 'js')));
        await gulp
        .src(["src/*"])
        .pipe(gulp.dest(path.join(outDir, 'src')));
}

async function setPackageVersion() {
    // Stamp the copy of the NPM package in outDir, but use this
    // source directory as a reference for calculating the git version.
    await nbgv.setPackageVersion(outDir, ".");
}

async function pack() {
    const config = process.env.BUILDCONFIGURATION || "Debug";
    const binDir = path.join(__dirname, `../../bin/Packages/${config}/npm`);
    await mkdirp(binDir);
    await ap.execAsync(`npm pack "${path.join(__dirname, outDir)}"`, { cwd: binDir });
}

exports.build = gulp.series(
    tsc,
    copyPackageContents,
    setPackageVersion,
    pack,
);

exports.default = exports.build;
