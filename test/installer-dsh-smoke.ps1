[CmdletBinding()]
param([Parameter(Mandatory)][string]$Node, [Parameter(Mandatory)][string]$Dsh, [Parameter(Mandatory)][string]$Pnpm)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/install.ps1')
$smokeBase = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.dsh-test'))
$smokeRoot = Join-Path $smokeBase ('installer-' + [guid]::NewGuid().ToString('N'))
$specialDirectory = Join-Path $smokeRoot (([char]0x684c).ToString() + ' space & (safe) %DSH_TEST_UNUSED%')
$savedStore = $env:npm_config_store_dir
$savedCache = $env:npm_config_cache
try {
    New-Item -ItemType Directory -Path $specialDirectory -Force | Out-Null
    $env:npm_config_store_dir = Join-Path $smokeRoot 'store'
    $env:npm_config_cache = Join-Path $smokeRoot 'npm-cache'
    [IO.File]::WriteAllText((Join-Path $specialDirectory 'package.json'), '{"name":"dsh-png-pet","version":"0.2.8","dsh":{"bundle":{"patch":"./cordis.patch.yml"}},"exports":{"./package.json":"./package.json","./cordis.patch.yml":"./cordis.patch.yml"}}')
    [IO.File]::WriteAllText((Join-Path $specialDirectory 'cordis.patch.yml'), '[]')
    $npm = Join-Path (Split-Path -Parent $Node) 'node_modules/npm/bin/npm-cli.js'
    Push-Location $specialDirectory
    try { Invoke-InstallerNative $Node @($npm, 'pack', '--ignore-scripts', '--pack-destination', $specialDirectory) | Out-Null }
    finally { Pop-Location }
    $runtime = [pscustomobject]@{ node = $Node; dsh = $Dsh; pnpm = $Pnpm; home = (Join-Path $specialDirectory 'isolated-dsh'); shim = (Join-Path $specialDirectory 'launcher'); bridge = (Join-Path $specialDirectory 'launcher/dsh-bridge.cjs') }
    Write-RuntimeBridge $runtime.shim
    Add-PetArchive $runtime (Join-Path $specialDirectory 'dsh-png-pet-0.2.8.tgz')
    if ((Get-InstalledPetVersion $runtime) -ne '0.2.8') { throw 'Real DSH did not install the fixture.' }
    Invoke-Dsh $runtime @('plugin', '--profile', 'web', 'remove', 'dsh-png-pet') | Out-Null
    if (Get-InstalledPetVersion $runtime) { throw 'Real DSH did not remove the fixture.' }
    Write-Output 'ISOLATED DSH INSTALL/REMOVE PASSED'
} finally {
    $env:npm_config_store_dir = $savedStore
    $env:npm_config_cache = $savedCache
    $resolved = [IO.Path]::GetFullPath($smokeRoot)
    if (-not $resolved.StartsWith($smokeBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid smoke cleanup target' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
