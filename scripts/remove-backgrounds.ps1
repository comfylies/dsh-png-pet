[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$InputDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [ValidateSet('u2net', 'u2net_human_seg', 'isnet-general-use', 'birefnet-general')]
    [string]$Model = 'u2net'
)

$ErrorActionPreference = 'Stop'

$resolvedInput = (Resolve-Path -LiteralPath $InputDirectory).Path
if (-not (Test-Path -LiteralPath $resolvedInput -PathType Container)) {
    throw "InputDirectory must be an existing directory."
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if ([System.StringComparer]::OrdinalIgnoreCase.Equals($resolvedInput, $resolvedOutput)) {
    throw "OutputDirectory must be different from InputDirectory so original files are preserved."
}

$localPython = Join-Path $PSScriptRoot '..\\.tools\\rembg\\Scripts\\python.exe'
if (-not (Test-Path -LiteralPath $localPython -PathType Leaf)) {
    throw "rembg is not installed. Run scripts\\install-rembg.ps1 first."
}

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
& $localPython (Join-Path $PSScriptRoot 'remove-backgrounds.py') $resolvedInput $resolvedOutput --model $Model
if ($LASTEXITCODE -ne 0) {
    throw "rembg failed with exit code $LASTEXITCODE."
}
