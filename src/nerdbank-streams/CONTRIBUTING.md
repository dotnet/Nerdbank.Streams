# Contributing to nerdbank-streams

## NPM packages

We use `yarn` to install NPM packages.
If you do not have yarn, you can obtain it with `npm i -g yarn`.
Simply run `yarn` in this directory to install dependencies.

## Editing

We recommend doing development within VS Code, with the Open Folder pointing at this directory rather than the repo root.
The folder recommends several VS Code extensions to install.

## Building

Run `yarn build` in this directory to transpile the Typescript files to Javascript.

## Testing

Run `yarn test` in this directory to run tests.
You must have transpiled first (using `yarn build` or `yarn tsc`).
Using `yarn watch` is a good way to ensure tsc has run with your changes automatically so you can run tests whenever you want.

You can also run tests from the (jest) Test Explorer in VS Code.
