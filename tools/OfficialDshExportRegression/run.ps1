param([string]$TaskDirectory = 'G:/DeepSeek DSH/DSH Works/Project/Dafeiyu-Go/Temp_SetupandLauncher/WindowAndExport-20261013/OfficialExport')
$ErrorActionPreference = 'Stop'
$task = [IO.Path]::GetFullPath($TaskDirectory)
if (-not $task.StartsWith('G:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Regression artifacts must stay on G:.' }
New-Item -ItemType Directory -Path $task -Force | Out-Null
$env:TEMP = $task
$env:TMP = $task
$env:DOTNET_CLI_HOME = Join-Path $task 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = 'G:/DeepSeek DSH/.nuget-packages'
$dotnet = 'G:/DeepSeek DSH/.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$artifacts = Join-Path $task 'artifacts'
& $dotnet build (Join-Path $PSScriptRoot 'OfficialDshExportRegression.csproj') -c Release --artifacts-path $artifacts --nologo
if ($LASTEXITCODE -ne 0) { throw 'Regression build failed.' }
$program = Get-ChildItem -LiteralPath (Join-Path $artifacts 'bin/OfficialDshExportRegression') -Recurse -Filter 'OfficialDshExportRegression.dll' | Select-Object -First 1 -ExpandProperty FullName
& $dotnet $program $task
if ($LASTEXITCODE -ne 0) { throw 'Official EXE regression failed.' }
