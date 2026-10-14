param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$framework = Split-Path $compiler
$env:TEMP = $output
$env:TMP = $output
$exe = Join-Path $output 'OfficialDshRestore.exe'
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/define:NETFRAMEWORK', "/out:$exe", "/win32manifest:$source/Restore.manifest",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll', '/r:System.Runtime.Serialization.dll', '/r:System.Web.Extensions.dll', '/r:System.Windows.Forms.dll', '/r:System.Xaml.dll',
    "/r:$framework/WPF/WindowsBase.dll", "/r:$framework/WPF/PresentationCore.dll", "/r:$framework/WPF/PresentationFramework.dll", "/resource:$source/Window.xaml,OfficialDshRestore.Window.xaml")
$icon = Join-Path $source '../../assets/DSHInstaller.ico'
if (Test-Path -LiteralPath $icon) { $arguments += "/win32icon:$icon" }
$arguments += (Get-ChildItem -LiteralPath $source -Filter '*.cs' | Select-Object -ExpandProperty FullName)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'OfficialDshRestore helper compilation failed.' }
Write-Output $exe
