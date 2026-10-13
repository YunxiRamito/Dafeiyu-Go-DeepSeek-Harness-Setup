param([switch]$ExistingImportRegression)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$parent = Split-Path $repo
$tempRoot = Join-Path $parent 'Temp_SetupandLauncher'
$runRoot = Join-Path $tempRoot ('OfficialImportRegression-' + [guid]::NewGuid().ToString('N'))
$dotnet = Join-Path $tempRoot 'DotnetSdk/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw "G: SDK not found: $dotnet" }
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$env:TEMP = $runRoot
$env:TMP = $runRoot
$env:DOTNET_CLI_HOME = Join-Path $tempRoot 'DotnetHome'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:NUGET_PACKAGES = Join-Path $tempRoot 'NugetPackages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $tempRoot 'NuGetHttpCache'
$env:NUGET_SCRATCH = Join-Path $runRoot 'nuget-scratch'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $tempRoot 'NuGetPluginsCache'
$env:DAFEIYU_INSTALLER_TEMP_ROOT = $tempRoot
try {
    $suites = @('migration', 'migration-options')
    if ($ExistingImportRegression) { $suites += 'existing-import' }
    foreach ($suite in $suites) {
        $properties = @()
        if ($suite -eq 'existing-import') {
            $source = Join-Path $parent 'Dafeiyu-Go-DeepSeek-Harness-Click-To-Run/tests/DshDataImport.Regression/Program.cs'
            if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Existing regression source not found: $source" }
            $properties = @("-p:RegressionSource=$source")
        }
        $artifacts = Join-Path $runRoot $suite
        $project = if ($suite -eq 'migration-options') { 'OfficialMigrationOptionsRegression' } else { 'DshDataImportRegression' }
        & $dotnet build (Join-Path $PSScriptRoot "$project.csproj") --artifacts-path $artifacts --verbosity minimal @properties
        if ($LASTEXITCODE -ne 0) { throw "$suite build failed" }
        & $dotnet (Join-Path $artifacts "bin/$project/debug/$project.dll")
        if ($LASTEXITCODE -ne 0) { throw "$suite failed" }
    }
}
finally {
    $resolved = [IO.Path]::GetFullPath($runRoot)
    $allowed = [IO.Path]::GetFullPath($tempRoot) + [IO.Path]::DirectorySeparatorChar
    if ($resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
