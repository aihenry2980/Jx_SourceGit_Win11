param(
    [string]$Version = $env:VERSION,
    [string]$Revision = "HEAD"
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d{8}(-\d+(st|nd|rd|th))?$') {
    throw "Expected a date-based package version, for example 20261002."
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "../..")).ProviderPath
$outputZip = Join-Path $repoRoot "build/sourcegit_${Version}.source-with-submodules.zip"
if (Test-Path -LiteralPath $outputZip) {
    throw "Source package already exists: $outputZip"
}

$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ("sourcegit-source-" + [Guid]::NewGuid().ToString("N"))
$sourceRoot = Join-Path $stageRoot "JxSourceGit-$Version"
$modules = [Collections.Generic.List[object]]::new()

function Export-Revision {
    param([string]$Repository, [string]$Commit, [string]$Destination, [string]$RelativePath)

    $archive = Join-Path $stageRoot ([Guid]::NewGuid().ToString("N") + ".zip")
    & git -C $Repository archive --format=zip "--output=$archive" $Commit
    if ($LASTEXITCODE -ne 0) { throw "Cannot archive $RelativePath at $Commit. Initialize all submodules first." }
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $Destination -Force
    Remove-Item -LiteralPath $archive -Force

    $entries = @(& git -C $Repository -c core.quotePath=false ls-tree -r $Commit)
    if ($LASTEXITCODE -ne 0) { throw "Cannot enumerate submodules in $RelativePath." }
    foreach ($entry in $entries) {
        if ($entry -match '^160000 commit ([0-9a-f]+)\t(.+)$') {
            $sha = $Matches[1]
            $path = $Matches[2]
            $modulePath = if ($RelativePath) { "$RelativePath/$path" } else { $path }
            $moduleRepository = Join-Path $Repository $path
            if (-not (Test-Path -LiteralPath (Join-Path $moduleRepository ".git"))) {
                throw "Submodule is not initialized: $modulePath"
            }
            $modules.Add([ordered]@{ Path = $modulePath; Commit = $sha })
            Export-Revision $moduleRepository $sha (Join-Path $Destination $path) $modulePath
        }
    }
}

try {
    [IO.Directory]::CreateDirectory($stageRoot) | Out-Null
    $commit = & git -C $repoRoot rev-parse "$Revision^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Cannot resolve source revision $Revision." }
    Export-Revision $repoRoot $commit $sourceRoot ""
    Set-Content -LiteralPath (Join-Path $sourceRoot "SOURCE_VERSION") -Value "v$Version" -Encoding ASCII
    [ordered]@{ Version = $Version; Commit = $commit; Submodules = @($modules.ToArray()) } |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $sourceRoot "source-manifest.json") -Encoding UTF8
    Compress-Archive -LiteralPath $sourceRoot -DestinationPath $outputZip
    Write-Host "[OK] Source package including $($modules.Count) submodule(s): $outputZip"
}
finally {
    $target = [IO.Path]::GetFullPath($stageRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $target.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($target) -notmatch '^sourcegit-source-[0-9a-f]{32}$') {
        throw "Refusing cleanup outside the temporary source staging directory."
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
