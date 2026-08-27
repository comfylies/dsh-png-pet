[CmdletBinding()]
param(
    [string]$PythonExecutable = 'python'
)

$ErrorActionPreference = 'Stop'

$python = Get-Command $PythonExecutable -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $python) {
    throw "Python 3.11+ is required. Install Python, then re-run with -PythonExecutable <path-to-python.exe>."
}

$version = & $python.Source -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')"
if ([version]$version -lt [version]'3.11') {
    throw "Python 3.11+ is required; found $version."
}

$environmentDirectory = Join-Path $PSScriptRoot '..\\.tools\\rembg'
$environmentPython = Join-Path $environmentDirectory 'Scripts\\python.exe'
if (-not (Test-Path -LiteralPath $environmentPython -PathType Leaf)) {
    & $python.Source -m venv $environmentDirectory
}

& $environmentPython -m pip install --upgrade pip 'rembg[cpu]==2.0.67'
& $environmentPython -c "import rembg; print(rembg.__version__)" | Out-Null

Write-Output "rembg is ready at $environmentDirectory"
