[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$Destination,
    [string]$BaseUrl
)
$ErrorActionPreference = 'Stop'
$archiveName = [IO.Path]::GetFileName($ArchivePath)
if ($archiveName -notmatch '^dsh-png-pet-(\d+\.\d+\.\d+)\.tgz$') { throw 'Invalid release archive name.' }
$version = $Matches[1]
$versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-versions.json') -Raw | ConvertFrom-Json
if ($BaseUrl) {
    $uri = $null
    if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
        throw 'BaseUrl must be an HTTPS directory without credentials, query or fragment.'
    }
    $BaseUrl = $BaseUrl.TrimEnd('/')
}
$release = [ordered]@{
    schemaVersion = 1
    name = 'dsh-png-pet'
    version = $version
    package = [ordered]@{ file = $archiveName; sha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() }
    node = [ordered]@{ version = $versions.nodeVersion; url = "https://nodejs.org/dist/v$($versions.nodeVersion)/node-v$($versions.nodeVersion)-win-x64.zip"; sha256 = $versions.nodeSha256 }
    dshVersion = $versions.dshVersion
    pnpmVersion = $versions.pnpmVersion
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$manifest = $release | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $Destination 'release.json'), $manifest)
$installer = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'install.ps1'))
$manifestUri = if ($BaseUrl) { "$BaseUrl/release.json" } else { '' }
$installer = $installer.Replace("`$DefaultManifestUri = '' # RELEASE_MANIFEST_URI", "`$DefaultManifestUri = '" + $manifestUri.Replace("'", "''") + "' # RELEASE_MANIFEST_URI")
[IO.File]::WriteAllText((Join-Path $Destination 'install.ps1'), $installer)
Write-Output 'Created install.ps1 and release.json. Publish these alongside the versioned tgz.'
if ($BaseUrl) { Write-Output "One-line install (after publishing): & ([scriptblock]::Create((irm '$BaseUrl/install.ps1')))" }
