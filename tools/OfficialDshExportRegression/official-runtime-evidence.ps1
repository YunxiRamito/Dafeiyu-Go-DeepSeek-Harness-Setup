param([Parameter(Mandatory=$true)][string]$OfficialRoot, [Parameter(Mandatory=$true)][string]$TaskDirectory)
$ErrorActionPreference = 'Stop'
$task = [IO.Path]::GetFullPath($TaskDirectory)
if (-not $task.StartsWith('G:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must stay on G:.' }
New-Item -ItemType Directory -Path $task -Force | Out-Null
$env:TEMP = $task
$env:TMP = $task
$root = [IO.Path]::GetFullPath($OfficialRoot)
$archive = Join-Path $root 'resources/app.asar'
$stream = [IO.File]::OpenRead($archive)
try {
    $reader = [IO.BinaryReader]::new($stream)
    $header = $reader.ReadBytes(16)
    $tree = [Text.Encoding]::UTF8.GetString($reader.ReadBytes([BitConverter]::ToUInt32($header,12))) | ConvertFrom-Json
    $baseOffset = 8L + [BitConverter]::ToUInt32($header,4)
    $evidence = @("OfficialRoot=$root", "ASAR_SHA256=$((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)")
    foreach ($path in @('lib/main.js', 'dsh/node_modules/@deepseek-ai/dsh-desktop-host/lib/index.js')) {
        $entry = $tree
        foreach ($segment in $path.Split('/')) { $entry = $entry.files.$segment }
        if (-not $entry -or $entry.unpacked) { throw "Expected packed official source: $path" }
        $stream.Position = $baseOffset + [long]$entry.offset
        $text = [Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$entry.size))
        $lines = $text -split "`n"
        $selected = [Collections.Generic.HashSet[int]]::new()
        for ($index = 0; $index -lt $lines.Length; $index++) {
            if ($lines[$index] -match 'profile: .*desktop|profile: join\(dshHome|const PROFILE_TEMPLATES|const WEB_PROFILE|createPluginProfile|initProfile\(projectDir, WEB_PROFILE|web: \{ bundles:') {
                for ($context = [Math]::Max(0,$index-1); $context -le [Math]::Min($lines.Length-1,$index+2); $context++) { $null = $selected.Add($context) }
            }
        }
        $evidence += "SOURCE $path"
        foreach ($index in ($selected | Sort-Object)) { $evidence += "$($index+1): $($lines[$index].TrimEnd())" }
    }
    $evidence += 'RUNTIME'
    $runtime = Get-Content -LiteralPath (Join-Path $root 'resources/runtime/primary-runtime/runtime.json') -Raw | ConvertFrom-Json
    $evidence += "desktopVersion=$($runtime.desktopVersion); Node=$($runtime.node); pnpm=$($runtime.pnpm)"
    $evidence | Set-Content -LiteralPath (Join-Path $task 'official-runtime-evidence.txt') -Encoding utf8
    $evidence
} finally { $stream.Dispose() }
