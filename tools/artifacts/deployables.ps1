$RepoRoot = [System.IO.Path]::GetFullPath("$PSScriptRoot\..\..")
$BuildConfiguration = $env:BUILDCONFIGURATION
if (!$BuildConfiguration) {
    $BuildConfiguration = 'Debug'
}

$PackagesRoot = "$RepoRoot/bin/Packages/$BuildConfiguration"
$Artifacts = @{}
if (Test-Path $PackagesRoot) {
    $Artifacts["$PackagesRoot"] = (Get-ChildItem $PackagesRoot -Recurse)
}

$Artifacts
