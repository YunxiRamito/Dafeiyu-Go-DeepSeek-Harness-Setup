param([Parameter(Mandatory=$true)][string]$OfficialRoot, [Parameter(Mandatory=$true)][string]$TaskDirectory)
$ErrorActionPreference = 'Stop'
$task = [IO.Path]::GetFullPath($TaskDirectory)
if (-not $task.StartsWith('G:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a G: task directory.' }
$root = [IO.Path]::GetFullPath($OfficialRoot)
$exe = Join-Path $root 'DeepSeek Harness.exe'
if (-not (Test-Path -LiteralPath (Join-Path $root 'resources/runtime/cli/bin/dsh.cmd'))) { throw 'Select an official desktop install.' }
$probe = Join-Path $task ('cli-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$profile = Join-Path $probe 'home/profiles/desktop'
New-Item -ItemType Directory -Path $profile -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $profile 'package.json'), '{"name":"dsh-profile-desktop","private":true,"dependencies":{},"dsh":{"profile":{"bundles":["@deepseek-ai/dsh-base","@deepseek-ai/dsh-web-app"]}}}', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $profile 'cordis.patch.yml'), "[]`n", [Text.UTF8Encoding]::new($false))
$env:DSH_HOME = Join-Path $probe 'home'
$env:DSH_AGENTS_HOME = Join-Path $probe 'agents'
$env:ELECTRON_RUN_AS_NODE = '1'
$env:TEMP = $probe
$env:TMP = $probe
$cli = Join-Path $root 'resources/app.asar/dsh/node_modules/@deepseek-ai/dsh-desktop-host/lib/cli.js'
$quotedCli = '"' + $cli + '"'
$args = @('--expose-internals', $quotedCli, 'plugin', '--profile', 'desktop', '--help')
$process = Start-Process -FilePath $exe -ArgumentList $args -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $probe 'stdout.txt') -RedirectStandardError (Join-Path $probe 'stderr.txt')
if (-not $process.WaitForExit(30000)) { throw 'Official CLI probe timed out.' }
[IO.File]::WriteAllText((Join-Path $probe 'result.txt'), "Exit=$($process.ExitCode)`nDSH_HOME=$env:DSH_HOME`nOfficialRoot=$root`nProbe=plugin --profile desktop --help`n")
Get-Content (Join-Path $probe 'stdout.txt') -TotalCount 8
Get-Content (Join-Path $probe 'stderr.txt') -TotalCount 12
Write-Output "CLI_EXIT=$($process.ExitCode) Evidence=$probe"
if ($process.ExitCode -ne 0) { throw 'Official clean desktop profile CLI probe failed.' }
