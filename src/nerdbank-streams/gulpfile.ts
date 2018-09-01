import * as gulp from "gulp";
import * as nbgv from "nerdbank-gitversioning";
import * as path from "path";
import * as ap from "./asyncprocess";

const outDir = "dist";

gulp.task("tsc", () => {
    return ap.execAsync(`node ./node_modules/typescript/bin/tsc -p tsconfig.json`, { cwd: __dirname });
});

gulp.task("copyPackageContents", ["tsc"], () => {
    return gulp
        .src([
            "package.json",
            "README.md",
            "out/*",
        ])
        .pipe(gulp.dest(outDir));
});

gulp.task("setPackageVersion", ["copyPackageContents"], () => {
    // Stamp the copy of the NPM package in outDir, but use this
    // source directory as a reference for calculating the git version.
    return nbgv.setPackageVersion(outDir, ".");
});

gulp.task("package", ["setPackageVersion"], () => {
    return ap.execAsync(`npm pack "${path.join(__dirname, outDir)}"`, { cwd: path.join(__dirname, "../../bin") });
});

gulp.task("default", ["package"], () => {
    // Nothing more to do here.
});
