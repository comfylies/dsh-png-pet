[CmdletBinding()]
param(
    [Parameter()]
    [string]$Destination,

    [Parameter()]
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$Destination = if ([string]::IsNullOrWhiteSpace($Destination)) {
    Join-Path $projectRoot 'dist/packages'
}
else {
    $Destination
}
$packageManifest = Get-Content -LiteralPath (Join-Path $projectRoot 'package.json') -Raw | ConvertFrom-Json
$packageName = $packageManifest.name
$packageVersion = $packageManifest.version
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$destinationRoot = [System.IO.Path]::GetPathRoot($destinationPath)

if ([string]::IsNullOrWhiteSpace($packageName) -or [string]::IsNullOrWhiteSpace($packageVersion)) {
    throw 'package.json must define a package name and version.'
}

if ($destinationPath.TrimEnd('\') -eq $destinationRoot.TrimEnd('\')) {
    throw 'The package destination must be a dedicated folder, not a drive root.'
}

Push-Location $projectRoot
try {
    if (-not $SkipTests) {
        & npm.cmd test
        if ($LASTEXITCODE -ne 0) { throw 'npm test failed; release package was not created.' }
    }

    & npm.cmd run build:helper
    if ($LASTEXITCODE -ne 0) { throw 'npm run build:helper failed; release package was not created.' }

    if (-not $SkipTests) {
        & npm.cmd run test:package
        if ($LASTEXITCODE -ne 0) { throw 'npm run test:package failed; release package was not created.' }
    }

    New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null
    & npm.cmd pack --pack-destination $destinationPath
    if ($LASTEXITCODE -ne 0) { throw 'npm pack failed.' }

    $archiveName = "$packageName-$packageVersion.tgz"
    $archivePath = Join-Path $destinationPath $archiveName
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw "npm pack did not create the expected archive: $archiveName"
    }

    Get-ChildItem -LiteralPath $destinationPath -File -Filter "$packageName-*.tgz" |
        Sort-Object -Property @{ Expression = 'LastWriteTimeUtc'; Descending = $true }, @{ Expression = 'Name'; Descending = $true } |
        Select-Object -Skip 5 |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

    Write-Output "Created release package: $archivePath"
    Write-Output "Retained the five newest $packageName archives in $destinationPath."
}
finally {
    Pop-Location
}
