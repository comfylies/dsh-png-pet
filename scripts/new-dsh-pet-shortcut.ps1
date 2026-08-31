[CmdletBinding()]
param(
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)) '启动 DSH 桌宠.lnk')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $projectRoot '启动 DSH 桌宠.vbs'
$wscript = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'wscript.exe'

if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw "找不到启动器：$launcher"
}

if (-not (Test-Path -LiteralPath $wscript -PathType Leaf)) {
    throw "找不到 Windows Script Host：$wscript"
}

if (Test-Path -LiteralPath $Destination) {
    throw "快捷方式已存在，未覆盖：$Destination"
}

$destinationDirectory = Split-Path -Parent $Destination
if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
    throw "目标目录不存在：$destinationDirectory"
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($Destination)
$shortcut.TargetPath = $wscript
$shortcut.Arguments = '//B //Nologo "' + $launcher + '"'
$shortcut.WorkingDirectory = $projectRoot
$shortcut.IconLocation = "$env:SystemRoot\System32\shell32.dll,220"
$shortcut.Description = '在后台启动 DeepSeek Harness，并显示 DSH PNG 桌宠'
$shortcut.Save()

Write-Host "已创建快捷方式：$Destination"
