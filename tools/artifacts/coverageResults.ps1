$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."

$ArtifactStagingFolder = & "$PSScriptRoot/../Get-ArtifactsStagingDirectory.ps1"
$directTestLogs = Join-Path $ArtifactStagingFolder test_logs
$testLogsPrefix = [IO.Path]::GetFullPath($directTestLogs).Replace('\', '/').TrimEnd('/') + '/'

$coverageFilesUnderRoot = @(Get-ChildItem "$RepoRoot/*.cobertura.xml" -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/]In[\\/]' -and
    !$_.FullName.Replace('\', '/').StartsWith($testLogsPrefix, [StringComparison]::OrdinalIgnoreCase)
})

# MTP writes both a standalone cobertura report and a TRX-attachment copy under In/.
$coverageFilesUnderArtifacts = @()
if (Test-Path $directTestLogs) {
    $reports = @(Get-ChildItem "$directTestLogs/*.cobertura.xml" -Recurse)
    $standaloneNames = @($reports | Where-Object { $_.FullName -notmatch '[\\/]In[\\/]' } | ForEach-Object { $_.Name })
    $coverageFilesUnderArtifacts = @($reports | Where-Object {
        $_.FullName -notmatch '[\\/]In[\\/]' -or $standaloneNames -notcontains $_.Name
    })
}

# Prepare code coverage reports for merging on another machine
Write-Host "Substituting $repoRoot with `"{reporoot}`""
@($coverageFilesUnderRoot + $coverageFilesUnderArtifacts) |? { $_ }|% {
    $content = Get-Content -LiteralPath $_ |% { $_ -Replace [regex]::Escape($repoRoot), "{reporoot}" }
    Set-Content -LiteralPath $_ -Value $content -Encoding UTF8
}

if (!((Test-Path $RepoRoot\bin) -and (Test-Path $RepoRoot\obj))) { return }

@{
    $directTestLogs = $coverageFilesUnderArtifacts;
    "$RepoRoot" = (
        $coverageFilesUnderRoot +
        (Get-ChildItem "$RepoRoot\obj\*.cs" -Recurse)
    );
}
