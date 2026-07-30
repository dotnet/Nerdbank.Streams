[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$BuildConfiguration = $env:BUILDCONFIGURATION
if (!$BuildConfiguration) {
    $BuildConfiguration = 'Debug'
}

$CrateSource = Join-Path $RepoRoot 'src/nerdbank-streams-rs'
$PackageRoot = Join-Path $RepoRoot 'obj/cargo-package'
$PackageSource = Join-Path $PackageRoot 'nerdbank-streams'
$OutputDirectory = Join-Path $RepoRoot "bin/Packages/$BuildConfiguration/cargo"

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PackageRoot
New-Item -ItemType Directory -Force -Path $PackageSource, $OutputDirectory | Out-Null
Copy-Item -Recurse -Force -Exclude target (Join-Path $CrateSource '*') $PackageSource

$Version = (dotnet tool run nbgv get-version -v SemVer2).Trim()
$ManifestPath = Join-Path $PackageSource 'Cargo.toml'
$Manifest = Get-Content -Raw $ManifestPath
$Manifest = $Manifest -replace '(?m)^version = ".*"$', ('version = "{0}"' -f $Version)
[System.IO.File]::WriteAllText($ManifestPath, $Manifest)

$LockfilePath = Join-Path $PackageSource 'Cargo.lock'
$Lockfile = Get-Content -Raw $LockfilePath
$Lockfile = $Lockfile -replace '(?s)(\[\[package\]\]\s+name = "nerdbank-streams"\s+version = ")[^"]+(")', ('${1}' + $Version + '$2')
[System.IO.File]::WriteAllText($LockfilePath, $Lockfile)

Push-Location $PackageSource
try {
    cargo package --locked
    if ($LASTEXITCODE) {
        throw "cargo package failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Copy-Item (Join-Path $PackageSource "target/package/nerdbank-streams-$Version.crate") $OutputDirectory -Force
