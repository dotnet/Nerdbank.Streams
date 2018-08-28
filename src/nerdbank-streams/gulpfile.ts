import * as ap from './asyncprocess';
import * as path from 'path';
import * as gulp from 'gulp';
import * as nbgv from 'nerdbank-gitversioning';
import * as ts from 'gulp-typescript';

const outDir = 'dist';
var tsProject = ts.createProject('tsconfig.json');

gulp.task('tsc', function() {
    return tsProject.src()
        .pipe(tsProject())
        .pipe(gulp.dest(tsProject.options.outDir));
});

gulp.task('copyPackageContents', ['tsc'], function() {
    return gulp
        .src([
            'package.json',
            'README.md',
            'out/*'
        ])
        .pipe(gulp.dest(outDir));
});

gulp.task('setPackageVersion', ['copyPackageContents'], function() {
    // Stamp the copy of the NPM package in outDir, but use this
    // source directory as a reference for calculating the git version.
    return nbgv.setPackageVersion(outDir, '.');
});

gulp.task('package', ['setPackageVersion'], function() {
    return ap.execAsync(`npm pack "${path.join(__dirname, outDir)}"`, { cwd: path.join(__dirname, '../../bin') });
});

gulp.task('default', ['package'], function() {
});
