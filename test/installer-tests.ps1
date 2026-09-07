param([Parameter(Mandatory)][string]$TestNode)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/install.ps1')

function Assert-That($Condition, $Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Action, $Pattern) {
    try { & $Action } catch { if ($_.Exception.Message -notmatch $Pattern) { throw }; return }
    throw "Expected error: $Pattern"
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('dsh-pet-install-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $artifact = Join-Path $testRoot 'dsh-png-pet-0.2.8.tgz'
    [IO.File]::WriteAllText($artifact, 'fixture')
    & (Join-Path $PSScriptRoot '../scripts/new-installer-release.ps1') -ArchivePath $artifact -Destination $testRoot
    $releaseInfo = Read-ReleaseManifest (Join-Path $testRoot 'release.json')
    Assert-That ($releaseInfo.version -eq '0.2.8') 'release version'
    Assert-That ($releaseInfo.package.sha256 -eq (Get-FileHash $artifact -Algorithm SHA256).Hash.ToLowerInvariant()) 'release hash'
    Assert-That ($releaseInfo.dshVersion -eq '0.1.1-rc.2') 'pinned DSH'
    $copy = Join-Path $testRoot 'copy.tgz'
    Receive-VerifiedArtifact $releaseInfo.package (Join-Path $testRoot 'release.json') $copy
    Assert-That (Test-Path -LiteralPath $copy) 'local package copy'
    $releaseInfo.package.sha256 = '0' * 64
    Assert-Throws { Receive-VerifiedArtifact $releaseInfo.package (Join-Path $testRoot 'release.json') $copy } 'checksum'
    $releaseInfo = Read-ReleaseManifest (Join-Path $testRoot 'release.json')
    $releaseInfo.schemaVersion = 99
    Assert-Throws { Assert-ReleaseManifest $releaseInfo } 'schema'
    $releaseInfo.schemaVersion = 1
    $releaseInfo.package.file = '../escape.tgz'
    Assert-Throws { Assert-ReleaseManifest $releaseInfo } 'package'
    Assert-Throws { Assert-HttpsUri 'http://example.com/release.json' } 'HTTPS'
    Assert-Throws { Assert-HttpsUri 'https://user:pass@example.com/release.json' } 'HTTPS'
    $releaseInfo = Read-ReleaseManifest (Join-Path $testRoot 'release.json')
    $releaseInfo.package | Add-Member -NotePropertyName url -NotePropertyValue 'https://example.com/unexpected.tgz'
    Assert-Throws { Assert-ReleaseManifest $releaseInfo } 'package'
    Assert-That (-not (Test-Path -LiteralPath ($copy + '.part'))) 'failed download leaves no partial file'
    Assert-That ([IO.File]::ReadAllText($copy) -eq 'fixture') 'checksum failure preserves verified destination'

    $published = Join-Path $testRoot 'published'
    & (Join-Path $PSScriptRoot '../scripts/new-installer-release.ps1') -ArchivePath $artifact -Destination $published -BaseUrl 'https://example.com/releases/v0.2.8/'
    Assert-That ([IO.File]::ReadAllText((Join-Path $published 'install.ps1')).Contains("`$DefaultManifestUri = 'https://example.com/releases/v0.2.8/release.json'")) 'inject manifest URL'
    Assert-Throws { & (Join-Path $PSScriptRoot '../scripts/new-installer-release.ps1') -ArchivePath $artifact -Destination $published -BaseUrl 'http://example.com' } 'HTTPS'

    $bridgeDirectory = Join-Path $testRoot (([char]0x684c).ToString() + ' space & (safe) %TEST_VAR%')
    Write-RuntimeBridge $bridgeDirectory
    $fakeDsh = Join-Path $bridgeDirectory 'fixture.cjs'
    [IO.File]::WriteAllText($fakeDsh, 'process.stdout.write(JSON.stringify(process.argv.slice(2)));')
    $bridgeRuntime = [pscustomobject]@{ node = $TestNode; dsh = $fakeDsh; pnpm = 'unused'; home = (Join-Path $testRoot 'isolated-home'); shim = $bridgeDirectory; bridge = (Join-Path $bridgeDirectory 'dsh-bridge.cjs') }
    $oldPath = $env:PATH
    $oldHome = $env:DSH_HOME
    $argv = (Invoke-Dsh $bridgeRuntime @('plugin', '--profile', 'web', 'add', '"%DSH_PET_PACKAGE%"')) | ConvertFrom-Json
    Assert-That ($argv.Count -eq 5 -and $argv[4] -eq '"%DSH_PET_PACKAGE%"') 'bridge must preserve literal shell quoting'
    Assert-That ($env:PATH -eq $oldPath -and $env:DSH_HOME -eq $oldHome) 'bridge restores environment'
    [IO.File]::WriteAllText($fakeDsh, 'process.exit(9);')
    Assert-Throws { Invoke-Dsh $bridgeRuntime @('web') } 'exit 9'
    Assert-That ($env:PATH -eq $oldPath -and $env:DSH_HOME -eq $oldHome) 'failed bridge restores environment'

    $dshPackageRoot = Join-Path $testRoot 'existing-dsh'
    New-Item -ItemType Directory -Path (Join-Path $dshPackageRoot 'lib') | Out-Null
    $candidate = Join-Path $dshPackageRoot 'lib/bin.js'
    [IO.File]::WriteAllText($candidate, '')
    [IO.File]::WriteAllText((Join-Path $dshPackageRoot 'package.json'), '{"name":"@deepseek-ai/dsh","version":"0.0.1"}')
    Assert-Throws { Resolve-InstallerRuntime (Read-ReleaseManifest (Join-Path $testRoot 'release.json')) $testRoot $candidate $false $null } 'not the tested version'

    $script:dshCalls = @()
    $script:installedVersion = $null
    $script:failAdd = $false
    function Invoke-Dsh($Runtime, [string[]]$Arguments) {
        $script:dshCalls += ,$Arguments
        if ($Arguments -contains 'add') {
            if ($script:failAdd) { $script:failAdd = $false; throw 'simulated install failure' }
            $script:installedVersion = if ($env:DSH_PET_PACKAGE -match 'old') { '0.2.7' } else { '0.2.8' }
        }
        if ($Arguments -contains 'remove') { $script:installedVersion = $null }
    }
    function Get-InstalledPetVersion($Runtime) { return $script:installedVersion }
    function Assert-PetStopped { }
    $runtime = @{}
    $oldPackage = Join-Path $testRoot 'old.tgz'
    [IO.File]::WriteAllText($oldPackage, 'old')
    $previous = [pscustomobject]@{ version = '0.2.7'; packagePath = $oldPackage; packageSha256 = (Get-FileHash $oldPackage).Hash }
    Install-PetPackage $runtime $artifact '0.2.8' $null
    Assert-That ($script:installedVersion -eq '0.2.8') 'fresh install'
    Assert-That ($script:dshCalls.Count -eq 1) 'must not remove before add'
    Assert-That ($script:dshCalls[0][-1] -eq '"%DSH_PET_PACKAGE%"') 'quoted environment path'
    Assert-That ($null -eq $env:DSH_PET_PACKAGE) 'restore transient path'
    $script:dshCalls = @()
    Install-PetPackage $runtime $artifact '0.2.8' $previous
    Assert-That ($script:dshCalls.Count -eq 0) 'same version must not reinstall'
    $script:installedVersion = '0.2.7'
    $script:failAdd = $true
    Assert-Throws { Install-PetPackage $runtime $artifact '0.2.8' $previous } 'restored'
    Assert-That ($script:installedVersion -eq '0.2.7') 'rollback restores prior package'

    $previous.packageSha256 = '0' * 64
    $script:failAdd = $true
    Assert-Throws { Install-PetPackage $runtime $artifact '0.2.8' $previous } 'could not be restored'
    $script:installedVersion = $null
    $script:failAdd = $true
    Assert-Throws { Install-PetPackage $runtime $artifact '0.2.8' $null } 'No verified previous'

    function Resolve-InstallerRuntime { return $bridgeRuntime }
    $managedRoot = Join-Path $testRoot 'managed-root'
    Start-PetInstall (Join-Path $testRoot 'release.json') $managedRoot '' $false $true $false
    Assert-That (Test-Path -LiteralPath (Join-Path $managedRoot 'launcher/launch.vbs')) 'stable VBS launcher'
    Assert-That (Test-Path -LiteralPath (Join-Path $managedRoot 'launcher/launch.ps1')) 'stable PowerShell launcher'
    $savedState = [IO.File]::ReadAllText((Join-Path $managedRoot 'install-state.json')) | ConvertFrom-Json
    Assert-That ($savedState.version -eq '0.2.8') 'verified installation persisted'
    $script:dshCalls = @()
    Start-PetInstall (Join-Path $testRoot 'release.json') $managedRoot '' $false $true $false
    Assert-That ($script:dshCalls.Count -eq 0) 'repeat install repairs launcher without changing plugin'
    $heldLock = [IO.File]::Open((Join-Path $managedRoot 'install.lock'), 'Open', 'ReadWrite', 'None')
    try { Assert-Throws { Start-PetInstall (Join-Path $testRoot 'release.json') $managedRoot '' $false $true $false } 'Another installer' }
    finally { $heldLock.Dispose() }
    Start-PetInstall '' $managedRoot '' $false $true $true
    Assert-That (-not (Test-Path -LiteralPath (Join-Path $managedRoot 'install-state.json'))) 'uninstall removes installation marker'
    Assert-That (Test-Path -LiteralPath (Join-Path $managedRoot 'cache/dsh-png-pet-0.2.8.tgz')) 'uninstall retains cached packages'

    Write-Output 'INSTALLER TESTS PASSED'
} finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test cleanup target' }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
