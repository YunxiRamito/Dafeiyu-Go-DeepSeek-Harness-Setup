param([string]$LauncherRepository = (Join-Path $PSScriptRoot '..\..\Dafeiyu-Go-DeepSeek-Harness-Click-To-Run'))

$ErrorActionPreference = 'Stop'
foreach ($core in 'DshDataImportService.cs','DshDataDiscoveryService.cs') {
    $launcherSource = Join-Path $LauncherRepository "source\Backup\$core"
    $installerSource = Join-Path $PSScriptRoot "..\src\DshInstaller\Shared\$core"
    if (-not (Test-Path -LiteralPath $launcherSource -PathType Leaf)) {
        throw "The launcher source is required for the cross-repository synchronization check: $launcherSource"
    }
    $launcherText = [IO.File]::ReadAllText($launcherSource).Replace("`r`n", "`n")
    $installerText = [IO.File]::ReadAllText($installerSource).Replace("`r`n", "`n")
    if ($launcherText -cne $installerText) {
        throw "DSH cores differ: $core. Synchronize the installer copy from launcher source/Backup before release."
    }
}
Write-Output 'PASS launcher and installer DSH import and discovery cores are identical.'
