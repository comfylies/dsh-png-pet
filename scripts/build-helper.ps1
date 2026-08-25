$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'pet-helper\PetHelper.csproj'
$publishDirectory = Join-Path $projectRoot 'pet-helper\bin\Release\net10.0-windows\win-x64\publish'
$runtimeDirectory = Join-Path $projectRoot 'runtime\bin\win32-x64'

dotnet restore $projectFile --configfile (Join-Path $projectRoot 'NuGet.Config') --force
dotnet publish $projectFile -c Release -r win-x64 --self-contained true --no-restore

New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $publishDirectory 'pet-helper.exe') -Destination (Join-Path $runtimeDirectory 'pet-helper.exe') -Force
