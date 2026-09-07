#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Manifest = '',
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'DSHPet'),
    [string]$DshEntry,
    [switch]$UsePrivateDsh,
    [switch]$NoShortcut,
    [switch]$Uninstall
)

# The release builder fills this in; source/local installations require -Manifest.
$DefaultManifestUri = '' # RELEASE_MANIFEST_URI

function Assert-HttpsUri([string]$Value) {
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Fragment) {
        throw 'A public HTTPS URL without credentials or fragment is required.'
    }
}

function Assert-ReleaseManifest($Release) {
    foreach ($field in $Release.PSObject.Properties.Name) {
        if ($field -notin @('schemaVersion', 'name', 'version', 'package', 'node', 'dshVersion', 'pnpmVersion')) { throw 'Unknown release schema field.' }
    }
    if ($Release.schemaVersion -ne 1) { throw 'Unsupported release schema.' }
    if ($Release.name -ne 'dsh-png-pet' -or $Release.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package identity.' }
    if ($Release.package.file -cne "dsh-png-pet-$($Release.version).tgz" -or
        $Release.package.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid package artifact.' }
    foreach ($field in $Release.package.PSObject.Properties.Name) {
        if ($field -notin @('file', 'sha256')) { throw 'Invalid package field.' }
    }
    foreach ($version in @($Release.node.version, $Release.dshVersion, $Release.pnpmVersion)) {
        if ($version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Invalid pinned dependency version.' }
    }
    if ($Release.node.version -notmatch '^24\.\d+\.\d+$' -or $Release.node.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid Node artifact.' }
    $expectedNodeUrl = "https://nodejs.org/dist/v$($Release.node.version)/node-v$($Release.node.version)-win-x64.zip"
    if ($Release.node.url -cne $expectedNodeUrl) { throw 'Invalid Node download URL.' }
}

function Receive-HttpsFile([string]$Url, [string]$Destination) {
    # Validate every redirect before following it, including GitHub release redirects.
    for ($redirect = 0; $redirect -le 8; $redirect++) {
        Assert-HttpsUri $Url
        $request = [Net.HttpWebRequest]::Create($Url)
        $request.AllowAutoRedirect = $false
        $request.Timeout = 60000
        $request.ReadWriteTimeout = 60000
        $request.UserAgent = 'DSHPet-Installer/1'
        $response = $request.GetResponse()
        try {
            $status = [int]$response.StatusCode
            if ($status -in @(301, 302, 303, 307, 308)) {
                $Url = ([Uri]::new([Uri]$Url, $response.Headers['Location'])).AbsoluteUri
                continue
            }
            if ($status -ne 200) { throw 'Artifact download did not return HTTP 200.' }
            $output = [IO.File]::Create($Destination)
            try { $response.GetResponseStream().CopyTo($output) } finally { $output.Dispose() }
            return
        } finally { $response.Dispose() }
    }
    throw 'Too many download redirects.'
}

function Read-ReleaseManifest([string]$Source) {
    if ($Source -match '^https?://') {
        Assert-HttpsUri $Source
        $temporaryManifest = [IO.Path]::GetTempFileName()
        try {
            Receive-HttpsFile $Source $temporaryManifest
            if ((Get-Item -LiteralPath $temporaryManifest).Length -gt 65536) { throw 'Release manifest is too large.' }
            $json = [IO.File]::ReadAllText($temporaryManifest)
        } finally { Remove-Item -LiteralPath $temporaryManifest -Force }
    } else {
        $json = [IO.File]::ReadAllText([IO.Path]::GetFullPath($Source))
    }
    if ($json.Length -gt 65536) { throw 'Release manifest is too large.' }
    $release = $json | ConvertFrom-Json
    Assert-ReleaseManifest $release
    return $release
}

function Assert-ArtifactHash([string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Expected) {
        throw 'Artifact checksum verification failed.'
    }
}

function Receive-VerifiedArtifact($Artifact, [string]$ManifestSource, [string]$Destination) {
    $part = $Destination + '.part'
    try {
        if ($Artifact.PSObject.Properties['url']) {
            $url = $Artifact.url
            Assert-HttpsUri $url
            Receive-HttpsFile $url $part
        } elseif ($ManifestSource -match '^https://') {
            $url = ([Uri]::new([Uri]$ManifestSource, $Artifact.file)).AbsoluteUri
            Assert-HttpsUri $url
            Receive-HttpsFile $url $part
        } else {
            $source = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($ManifestSource))) $Artifact.file
            Copy-Item -LiteralPath $source -Destination $part -Force
        }
        Assert-ArtifactHash $part $Artifact.sha256
        Move-Item -LiteralPath $part -Destination $Destination -Force
    } finally {
        if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force }
    }
}

function Invoke-InstallerNative([string]$Command, [string[]]$Arguments) {
    # Keep dependency/CLI output in memory; do not write it to an installation log.
    $ErrorActionPreference = 'Continue'
    $output = & $Command @Arguments 2>$null
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "An installation command failed (exit $code). Check network access and dependency compatibility, then retry." }
    return ($output -join "`n")
}

function Get-CompatibleNode($Release, [string]$Root) {
    $candidates = @()
    $command = Get-Command node.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    $privateNode = Join-Path $Root "runtime/node-v$($Release.node.version)-win-x64/node.exe"
    $candidates += $privateNode
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $candidate) 'node_modules/npm/bin/npm-cli.js'))) { continue }
        try {
            $version = (Invoke-InstallerNative $candidate @('--version')).Trim()
            if ($version -match '^v(24\.\d+\.\d+)$' -and [version]$Matches[1] -ge [version]$Release.node.version) { return $candidate }
        } catch { }
    }
    Write-Host '[2/5] Preparing the private Node runtime...'
    $archive = Join-Path $Root 'cache/node.zip'
    Receive-VerifiedArtifact $Release.node '' $archive
    $runtimeDir = Join-Path $Root 'runtime'
    Expand-Archive -LiteralPath $archive -DestinationPath $runtimeDir -Force
    $version = (Invoke-InstallerNative $privateNode @('--version')).Trim()
    if ($version -cne "v$($Release.node.version)") { throw 'The installed Node version does not match the release.' }
    return $privateNode
}

function Get-DshEntryVersion([string]$Entry) {
    if (-not (Test-Path -LiteralPath $Entry -PathType Leaf)) { return $null }
    $packagePath = Join-Path (Split-Path -Parent (Split-Path -Parent $Entry)) 'package.json'
    if (-not (Test-Path -LiteralPath $packagePath)) { return $null }
    $package = [IO.File]::ReadAllText($packagePath) | ConvertFrom-Json
    if ($package.name -ne '@deepseek-ai/dsh') { return $null }
    return $package.version
}

function Find-DshEntry {
    $command = Get-Command dsh.cmd -ErrorAction SilentlyContinue
    $candidates = @()
    if ($command) {
        $directory = Split-Path -Parent $command.Source
        $candidates += (Join-Path $directory 'node_modules/@deepseek-ai/dsh/lib/bin.js')
        $candidates += (Join-Path $directory '../@deepseek-ai/dsh/lib/bin.js')
    }
    $candidates += (Join-Path $env:APPDATA 'npm/node_modules/@deepseek-ai/dsh/lib/bin.js')
    foreach ($candidate in $candidates) {
        if (Get-DshEntryVersion $candidate) { return [IO.Path]::GetFullPath($candidate) }
    }
    if ($command) { throw 'An existing DSH installation could not be resolved. Pass -DshEntry with its lib/bin.js, or choose -UsePrivateDsh.' }
    return $null
}

function Resolve-InstallerRuntime($Release, [string]$Root, [string]$ExplicitEntry, [bool]$Private, $Previous) {
    $entry = $null
    if (-not $Private) {
        if ($ExplicitEntry) { $entry = [IO.Path]::GetFullPath($ExplicitEntry) }
        elseif ($Previous -and $Previous.runtime.PSObject.Properties['managed'] -and $Previous.runtime.managed) { $entry = $null }
        elseif ($Previous -and (Test-Path -LiteralPath $Previous.runtime.dsh)) { $entry = $Previous.runtime.dsh }
        else { $entry = Find-DshEntry }
        if ($entry -and (Get-DshEntryVersion $entry) -ne $Release.dshVersion) {
            throw "Existing DSH is not the tested version $($Release.dshVersion). Use -UsePrivateDsh for a separate environment; the existing DSH was not changed."
        }
    }
    $node = Get-CompatibleNode $Release $Root
    $toolDir = Join-Path $Root "host/dsh-$($Release.dshVersion)-pnpm-$($Release.pnpmVersion)"
    $pnpm = Join-Path $toolDir 'node_modules/pnpm/bin/pnpm.cjs'
    $privateEntry = Join-Path $toolDir 'node_modules/@deepseek-ai/dsh/lib/bin.js'
    $packages = @()
    if (-not $entry) { $entry = $privateEntry; if ((Get-DshEntryVersion $entry) -ne $Release.dshVersion) { $packages += "@deepseek-ai/dsh@$($Release.dshVersion)" } }
    $pnpmManifest = Join-Path $toolDir 'node_modules/pnpm/package.json'
    if (-not (Test-Path -LiteralPath $pnpmManifest) -or
        ([IO.File]::ReadAllText($pnpmManifest) | ConvertFrom-Json).version -ne $Release.pnpmVersion) {
        $packages += "pnpm@$($Release.pnpmVersion)"
    }
    if ($packages.Count -gt 0) {
        $npm = Join-Path (Split-Path -Parent $node) 'node_modules/npm/bin/npm-cli.js'
        if (-not (Test-Path -LiteralPath $npm)) { throw 'The selected Node installation has no npm CLI. Repair Node and retry.' }
        New-Item -ItemType Directory -Path $toolDir -Force | Out-Null
        Write-Host '[2/5] Installing the pinned DSH/pnpm dependencies (this can take several minutes)...'
        $savedPath = $env:PATH
        try {
            $env:PATH = (Split-Path -Parent $node) + ';' + $env:PATH
            Invoke-InstallerNative $node (@($npm, 'install', '--prefix', $toolDir, '--save-exact', '--no-audit', '--no-fund') + $packages) | Out-Null
        } finally { $env:PATH = $savedPath }
    }
    if ((Get-DshEntryVersion $entry) -ne $Release.dshVersion -or -not (Test-Path -LiteralPath $pnpm)) { throw 'Dependency installation verification failed.' }
    $dshHome = $env:DSH_HOME
    if ($Previous -and -not $Private) { $dshHome = $Previous.runtime.home }
    if ($Private) { $dshHome = Join-Path $Root 'dsh-data' }
    if ($dshHome) {
        if ($dshHome -eq '~') { $dshHome = [Environment]::GetFolderPath('UserProfile') }
        elseif ($dshHome -match '^~[/\\]') { $dshHome = Join-Path ([Environment]::GetFolderPath('UserProfile')) $dshHome.Substring(2) }
        $dshHome = [IO.Path]::GetFullPath($dshHome)
    }
    return [pscustomobject]@{ node = $node; dsh = $entry; pnpm = $pnpm; home = $dshHome; managed = ($entry -eq $privateEntry); bridge = (Join-Path $Root 'launcher/dsh-bridge.cjs'); shim = (Join-Path $Root 'launcher') }
}

function Write-RuntimeBridge([string]$Directory) {
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $Directory 'dsh-bridge.cjs'), @'
const { spawnSync } = require('node:child_process');
const args = JSON.parse(process.env.DSH_PET_ARGS);
const result = spawnSync(process.execPath, [process.env.DSH_PET_DSH, ...args], { stdio: 'inherit', windowsHide: true });
process.exit(result.status ?? 1);
'@)
    [IO.File]::WriteAllText((Join-Path $Directory 'pnpm.cmd'), '@"%DSH_PET_NODE%" "%DSH_PET_PNPM%" %*' + "`r`n")
}

function Invoke-Dsh($Runtime, [string[]]$Arguments) {
    $names = @('PATH', 'DSH_HOME', 'DSH_PET_NODE', 'DSH_PET_DSH', 'DSH_PET_PNPM', 'DSH_PET_ARGS')
    $saved = @{}
    foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    try {
        $env:PATH = $Runtime.shim + ';' + (Split-Path -Parent $Runtime.node) + ';' + $env:PATH
        $env:DSH_HOME = $Runtime.home
        $env:DSH_PET_NODE = $Runtime.node
        $env:DSH_PET_DSH = $Runtime.dsh
        $env:DSH_PET_PNPM = $Runtime.pnpm
        $env:DSH_PET_ARGS = ConvertTo-Json -InputObject @($Arguments) -Compress
        return Invoke-InstallerNative $Runtime.node @($Runtime.bridge)
    } finally {
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    }
}

function Get-InstalledPetVersion($Runtime) {
    $json = Invoke-Dsh $Runtime @('plugin', '--profile', 'web', 'list', 'dsh-png-pet', '--depth', '0', '--json')
    $items = @($json | ConvertFrom-Json)
    foreach ($item in $items) {
        if ($item.PSObject.Properties['dependencies'] -and $item.dependencies.PSObject.Properties['dsh-png-pet']) {
            return $item.dependencies.'dsh-png-pet'.version
        }
    }
    return $null
}

function Assert-PetStopped {
    if (Get-Process -Name pet-helper -ErrorAction SilentlyContinue) {
        throw 'Close DSH and the desktop pet normally, then rerun this command. No running process was stopped.'
    }
}

function Add-PetArchive($Runtime, [string]$Archive) {
    $savedPackage = $env:DSH_PET_PACKAGE
    try {
        # DSH forwards through cmd.exe: expand the path inside quotes in that shell,
        # rather than sending a raw path that DSH would concatenate without quoting.
        $env:DSH_PET_PACKAGE = [IO.Path]::GetFullPath($Archive)
        Invoke-Dsh $Runtime @('plugin', '--profile', 'web', 'add', '"%DSH_PET_PACKAGE%"') | Out-Null
    } finally { $env:DSH_PET_PACKAGE = $savedPackage }
}

function Install-PetPackage($Runtime, [string]$Archive, [string]$Version, $Previous) {
    $installed = Get-InstalledPetVersion $Runtime
    if ($installed -eq $Version) { return }
    Assert-PetStopped
    try {
        Add-PetArchive $Runtime $Archive
        if ((Get-InstalledPetVersion $Runtime) -ne $Version) { throw 'Installed plugin version did not match.' }
    } catch {
        if ($Previous -and $installed -eq $Previous.version) {
            try {
                Assert-ArtifactHash $Previous.packagePath $Previous.packageSha256
                Add-PetArchive $Runtime $Previous.packagePath
                if ((Get-InstalledPetVersion $Runtime) -ne $installed) { throw 'Rollback version mismatch.' }
            } catch { throw 'Installation failed and the previous plugin could not be restored. Keep the cached packages and retry after checking dependencies.' }
            throw 'Installation failed; the previous plugin was restored. Retry after checking network access.'
        }
        throw 'Installation failed. No verified previous package is available for automatic recovery. Existing DSH data was not removed; retry the installer.'
    }
}

function Write-InstalledLauncher([string]$Root, [bool]$SkipShortcut) {
    $directory = Join-Path $Root 'launcher'
    [IO.File]::WriteAllText((Join-Path $directory 'launch.ps1'), @'
$ErrorActionPreference = 'Stop'
try {
    $state = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../install-state.json')) | ConvertFrom-Json
    $runtime = $state.runtime
    $env:PATH = $runtime.shim + ';' + (Split-Path -Parent $runtime.node) + ';' + $env:PATH
    $env:DSH_HOME = $runtime.home
    $env:DSH_PET_NODE = $runtime.node
    $env:DSH_PET_PNPM = $runtime.pnpm
    $env:DSH_PET_DSH = $runtime.dsh
    $env:DSH_PET_ARGS = '["web","--no-open"]'
    & $runtime.node $runtime.bridge
    if ($LASTEXITCODE -ne 0) { throw 'DSH could not start. Rerun the installer to repair the environment.' }
} catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($_.Exception.Message, 'DSH Pet') | Out-Null
}
'@)
    [IO.File]::WriteAllText((Join-Path $directory 'launch.vbs'), @'
Option Explicit
Dim shell, fs, script, powershell
Set shell = CreateObject("WScript.Shell")
Set fs = CreateObject("Scripting.FileSystemObject")
script = fs.BuildPath(fs.GetParentFolderName(WScript.ScriptFullName), "launch.ps1")
powershell = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
shell.Run Chr(34) & powershell & Chr(34) & " -NoProfile -ExecutionPolicy Bypass -File " & Chr(34) & script & Chr(34), 0, False
'@)
    if (-not $SkipShortcut) {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DSH Pet.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $launcher = Join-Path $directory 'launch.vbs'
        if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.Arguments -ne ('//B //Nologo "' + $launcher + '"')) {
            Write-Warning 'An unrelated DSH Pet shortcut exists. It was not overwritten.'
            return
        }
        $shortcut.TargetPath = Join-Path $env:SystemRoot 'System32/wscript.exe'
        $shortcut.Arguments = '//B //Nologo "' + $launcher + '"'
        $shortcut.WorkingDirectory = $Root
        $shortcut.Description = 'Start DSH and its desktop pet'
        $shortcut.Save()
    }
}

function Uninstall-Pet($State, [string]$Root) {
    Assert-PetStopped
    Invoke-Dsh $State.runtime @('plugin', '--profile', 'web', 'remove', 'dsh-png-pet') | Out-Null
    if (Get-InstalledPetVersion $State.runtime) { throw 'Plugin removal could not be verified.' }
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DSH Pet.lnk'
    if (Test-Path -LiteralPath $shortcutPath) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ($shortcut.Arguments -eq ('//B //Nologo "' + (Join-Path $Root 'launcher/launch.vbs') + '"')) { Remove-Item -LiteralPath $shortcutPath }
    }
    Remove-Item -LiteralPath (Join-Path $Root 'install-state.json')
    Write-Host 'Desktop pet removed. DSH, sessions, credentials and cached packages were retained.'
}

function Start-PetInstall([string]$Source, [string]$Root, [string]$ExplicitEntry, [bool]$Private, [bool]$SkipShortcut, [bool]$Remove) {
    $ErrorActionPreference = 'Stop'
    $architecture = [Microsoft.Win32.Registry]::GetValue('HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'PROCESSOR_ARCHITECTURE', '')
    if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitOperatingSystem -or $architecture -ne 'AMD64' -or
        [Environment]::OSVersion.Version.Major -lt 10) { throw 'Windows 10/11 x64 is required.' }
    $Root = [IO.Path]::GetFullPath($Root)
    if ($Root.TrimEnd('\') -eq [IO.Path]::GetPathRoot($Root).TrimEnd('\')) { throw 'Choose a dedicated installation directory.' }
    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    $lock = $null
    try { $lock = [IO.File]::Open((Join-Path $Root 'install.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another installer is running, or the installation directory is not writable.' }
    $savedTls = [Net.ServicePointManager]::SecurityProtocol
    try {
        [Net.ServicePointManager]::SecurityProtocol = $savedTls -bor [Net.SecurityProtocolType]::Tls12
        $statePath = Join-Path $Root 'install-state.json'
        $previous = $null
        if (Test-Path -LiteralPath $statePath) { $previous = [IO.File]::ReadAllText($statePath) | ConvertFrom-Json }
        if ($Remove) {
            if ($previous) { Uninstall-Pet $previous $Root } else { Write-Host 'No managed desktop-pet installation was found.' }
            return
        }
        if (-not $Source) { throw 'No release URL is configured. Pass -Manifest with release.json or use the published installer.' }
        if ($previous -and $Private -and $previous.runtime.home -ne (Join-Path $Root 'dsh-data')) {
            throw 'Use a different -InstallRoot when creating a separate DSH environment.'
        }
        Write-Host '[1/5] Checking the release...'
        $release = Read-ReleaseManifest $Source
        if ($previous -and $previous.version -eq $release.version -and $previous.packageSha256 -ine $release.package.sha256) {
            throw 'This release changed an existing version. Publish a new version instead of replacing an installed artifact.'
        }
        New-Item -ItemType Directory -Path (Join-Path $Root 'cache') -Force | Out-Null
        $archive = Join-Path $Root ('cache/' + $release.package.file)
        Receive-VerifiedArtifact $release.package $Source $archive
        Write-RuntimeBridge (Join-Path $Root 'launcher')
        $runtime = Resolve-InstallerRuntime $release $Root $ExplicitEntry $Private $previous
        Write-Host '[3/5] Installing and verifying the desktop-pet plugin...'
        Install-PetPackage $runtime $archive $release.version $previous
        $state = [ordered]@{ schemaVersion = 1; version = $release.version; runtime = $runtime; packagePath = $archive; packageSha256 = $release.package.sha256 }
        $pendingState = $statePath + '.tmp'
        [IO.File]::WriteAllText($pendingState, ($state | ConvertTo-Json -Depth 5))
        Move-Item -LiteralPath $pendingState -Destination $statePath -Force
        Write-Host '[4/5] Preparing the launcher...'
        Write-InstalledLauncher $Root $SkipShortcut
        Write-Host '[5/5] Installed. Start DSH Pet from the desktop shortcut. For first use, open DSH from the pet menu to configure your model and workspace. Restart an existing DSH Host to load this version.'
    } finally {
        [Net.ServicePointManager]::SecurityProtocol = $savedTls
        if ($lock) { $lock.Dispose() }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Manifest) { $Manifest = $DefaultManifestUri }
    Start-PetInstall $Manifest $InstallRoot $DshEntry $UsePrivateDsh.IsPresent $NoShortcut.IsPresent $Uninstall.IsPresent
}
