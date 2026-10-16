param(
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$RidDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$work = [IO.Path]::GetFullPath($WorkDirectory)
$workspace = [IO.Path]::GetFullPath((Join-Path $root '..')).TrimEnd('\') + '\'
if (-not $work.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)) {
    throw 'Use a new test directory under the project workspace.'
}
New-Item -ItemType Directory -Path $work | Out-Null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'pack-release.ps1'), [ref]$null, [ref]$errors)
if ($errors.Count) { throw 'Pack script does not parse.' }
foreach ($name in @('Get-InstallerXamlResources', 'Assert-InstallerPayloadResources', 'Copy-InstallerXamlResources', 'Assert-InstallerPayloadArchive')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name }, $false)
    if (-not $function) { throw "Missing production function: $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}
$results = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [scriptblock]$Body) {
    & $Body
    $results.Add([ordered]@{ name = $Name; passed = $true })
    Write-Output "PASS $Name"
}
function Reject([scriptblock]$Body) {
    $rejected = $false
    try { & $Body } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected incomplete/invalid resources to be rejected.' }
}
$required = @(Get-InstallerXamlResources (Join-Path $root 'src/DshInstaller'))
$pri = 'DSH-Installer.pri'
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Path (Join-Path $payload 'win-x64') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $payload 'win-x64/stale.xbf'), 'STALE')
Check 'inventory contains current 22 distinct XAML files at their load paths' {
    if ($required.Count -ne 22 -or 'App.xbf' -notin $required -or 'Pages\ImportPage.xbf' -notin $required -or 'Styles\Theme.xbf' -notin $required) { throw 'Unexpected current XAML source inventory.' }
}
Check 'copy current RID resources to root and remove stale nested RID' {
    Copy-InstallerXamlResources $RidDirectory $payload $required $pri
    if (Test-Path -LiteralPath (Join-Path $payload 'win-x64')) { throw 'Stale RID directory survived.' }
    foreach ($name in $required + @($pri)) {
        if ((Get-FileHash -LiteralPath (Join-Path $RidDirectory $name)).Hash -cne (Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash) { throw "Resource differs: $name" }
    }
}
Check 'missing required page fails even with many unrelated XBF files' {
    $missing = Join-Path $payload 'Pages/ImportPage.xbf'
    Move-Item -LiteralPath $missing -Destination ($missing + '.saved')
    try {
        1..30 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $payload ("decoy-$_.xbf")), 'decoy') }
        Reject { Assert-InstallerPayloadResources $payload $required $pri }
    } finally { Move-Item -LiteralPath ($missing + '.saved') -Destination $missing }
}
Check 'empty App resource fails' {
    $app = Join-Path $payload 'App.xbf'; $bytes = [IO.File]::ReadAllBytes($app)
    try { [IO.File]::WriteAllBytes($app, [byte[]]@()); Reject { Assert-InstallerPayloadResources $payload $required $pri } }
    finally { [IO.File]::WriteAllBytes($app, $bytes) }
}
Check 'missing exact root PRI fails' {
    $path = Join-Path $payload $pri; Move-Item -LiteralPath $path -Destination ($path + '.saved')
    try { Reject { Assert-InstallerPayloadResources $payload $required $pri } }
    finally { Move-Item -LiteralPath ($path + '.saved') -Destination $path }
}
Check 'Compose resource validation rejects nested RID residue' {
    $path = Join-Path $payload 'win-x64'; New-Item -ItemType Directory -Path $path | Out-Null
    try { Reject { Assert-InstallerPayloadResources $payload $required $pri } }
    finally { Remove-Item -LiteralPath $path }
}
Check 'incomplete RID fails before modifying destination' {
    $bad = Join-Path $work 'bad-rid'; New-Item -ItemType Directory -Path $bad | Out-Null
    $unused = Join-Path $work 'unused-destination'
    Reject { Copy-InstallerXamlResources $bad $unused $required $pri }
    if (Test-Path -LiteralPath $unused) { throw 'Incomplete RID modified output.' }
}
Check 'new source page automatically becomes a required XBF' {
    $future = Join-Path $work 'future-source'; New-Item -ItemType Directory -Path (Join-Path $future 'Pages') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $future 'App.xaml'), '<Application/>')
    [IO.File]::WriteAllText((Join-Path $future 'Pages/FuturePage.xaml'), '<Page/>')
    $futureRequired = @(Get-InstallerXamlResources $future)
    if ($futureRequired.Count -ne 2 -or 'Pages\FuturePage.xbf' -notin $futureRequired) { throw 'New XAML not included.' }
    Reject { Assert-InstallerPayloadResources $payload $futureRequired $pri }
}
$archive = Join-Path $work 'payload.zip'
Check 'final ZIP has every exact root resource' {
    Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive
    Assert-InstallerPayloadArchive $archive $required $pri
}
Check 'final ZIP with duplicated required resource fails' {
    $duplicateArchive = Join-Path $work 'duplicate.zip'; Copy-Item -LiteralPath $archive -Destination $duplicateArchive
    $zip = [IO.Compression.ZipFile]::Open($duplicateArchive, [IO.Compression.ZipArchiveMode]::Update)
    try { $entry = $zip.CreateEntry('App.xbf'); $stream = $entry.Open(); try { $stream.WriteByte(1) } finally { $stream.Dispose() } } finally { $zip.Dispose() }
    Reject { Assert-InstallerPayloadArchive $duplicateArchive $required $pri }
}
Check 'final ZIP missing a required page fails' {
    $missingArchive = Join-Path $work 'missing.zip'; Copy-Item -LiteralPath $archive -Destination $missingArchive
    $zip = [IO.Compression.ZipFile]::Open($missingArchive, [IO.Compression.ZipArchiveMode]::Update)
    try { $entry = @($zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq 'Pages/ImportPage.xbf' })[0]; $entry.Delete() } finally { $zip.Dispose() }
    Reject { Assert-InstallerPayloadArchive $missingArchive $required $pri }
}
Check 'final ZIP with nested RID residue fails' {
    $nestedArchive = Join-Path $work 'nested.zip'; Copy-Item -LiteralPath $archive -Destination $nestedArchive
    $zip = [IO.Compression.ZipFile]::Open($nestedArchive, [IO.Compression.ZipArchiveMode]::Update)
    try { $entry = $zip.CreateEntry('win-x64/stale.xbf'); $stream = $entry.Open(); try { $stream.WriteByte(1) } finally { $stream.Dispose() } } finally { $zip.Dispose() }
    Reject { Assert-InstallerPayloadArchive $nestedArchive $required $pri }
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'results.json') -Encoding utf8
Write-Output ("PASS all $($results.Count) resource packaging checks; no build or official output changed.")
