# Contributing

## Best practices

* Use Windows PowerShell or [PowerShell Core][pwsh] (including on Linux/OSX) to run .ps1 scripts.
  Some scripts set environment variables to help you, but they are only retained if you use PowerShell as your shell.

## Prerequisites

The only prerequisite for building, testing, and deploying from this repository
is the [.NET SDK](https://get.dot.net/).
You should install the version specified in `global.json` or a later version within
the same major.minor.Bxx "hundreds" band.
For example if 2.2.300 is specified, you may install 2.2.300, 2.2.301, or 2.2.310
while the 2.2.400 version would not be considered compatible by .NET SDK.
See [.NET Core Versioning](https://docs.microsoft.com/dotnet/core/versions/) for more information.

All dependencies can be installed by running the `init.ps1` script at the root of the repository
using Windows PowerShell or [PowerShell Core][pwsh] (on any OS).

This repository can be built on Windows, Linux, and OSX.

## Package restore

The easiest way to restore packages may be to run `init.ps1` which automatically authenticates
to the feeds that packages for this repo come from, if any.
`dotnet restore` or `nuget restore` also work but may require extra steps to authenticate to any applicable feeds.

## Building

Building, testing, and packing this repository can be done by using the standard dotnet CLI commands (e.g. `dotnet build`, `dotnet test`, `dotnet pack`, etc.).

## The `nerdbank-streams` NPM package

The `nerdbank-streams` NPM package builds out of the `src/nerdbank-streams` directory.
Please review the [CONTRIBUTING.md](src/nerdbank-streams/CONTRIBUTING.md) document in that directory for instructions.

[pwsh]: https://docs.microsoft.com/powershell/scripting/install/installing-powershell?view=powershell-6

## Tutorial and API documentation

API and hand-written docs are found under the `docfx/` directory. and are built by [docfx](https://dotnet.github.io/docfx/).

You can make changes and host the site locally to preview them by switching to that directory and running the `dotnet docfx --serve` command.
After making a change, you can rebuild the docs site while the localhost server is running by running `dotnet docfx` again from a separate terminal.

The `.github/workflows/docs.yml` GitHub Actions workflow publishes the content of these docs to github.io if the workflow itself and [GitHub Pages is enabled for your repository](https://docs.github.com/en/pages/quickstart).
