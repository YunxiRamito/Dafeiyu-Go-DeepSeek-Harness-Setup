param([string]$TaskDirectory = 'G:/DeepSeek DSH/DSH Works/Project/Dafeiyu-Go/Temp_SetupandLauncher/WindowAndExport-20261013/OfficialExport')
$ErrorActionPreference = 'Stop'
$task = [IO.Path]::GetFullPath($TaskDirectory)
if (-not $task.StartsWith('G:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'UI artifacts must stay on G:.' }
$env:TEMP = $task
$env:TMP = $task
$fixture = Get-ChildItem -LiteralPath $task -Directory -Filter 'f-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $fixture) { throw 'Run run.ps1 first.' }
$exe = Join-Path $fixture.FullName 'exports/migration.exe'
$env:DSH_HOME = Join-Path $fixture.FullName 'target-home'
$env:DSH_AGENTS_HOME = Join-Path $fixture.FullName 'target-agents'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class OfficialRestoreUiNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
}
'@
$process = Start-Process -FilePath $exe -WorkingDirectory $fixture.FullName -WindowStyle Normal -PassThru
try {
    $null = $process.WaitForInputIdle(10000)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) { throw "WPF helper exited before showing UI: $($process.ExitCode)" }
        $handle = $process.MainWindowHandle
    } while ($handle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
    if ($handle -eq [IntPtr]::Zero) { throw 'WPF main window did not appear.' }
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $controls = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $validated = $controls | Where-Object { $_.Current.Name -eq '数据包校验通过' }
        if (-not $validated) { Start-Sleep -Milliseconds 200 }
    } while (-not $validated -and [DateTime]::UtcNow -lt $deadline)
    if (-not $validated) { throw 'Payload validation did not finish successfully in the GUI.' }
    $names = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
    if ($element.Current.Name -notmatch '数据还原' -or $names.Count -lt 12) { throw "Wrong WPF window or too few accessible controls: $($element.Current.Name), count=$($names.Count), names=$($names -join '|')" }
    $null = [OfficialRestoreUiNative]::SetForegroundWindow($handle)
    foreach ($size in @(@(0,0,'default'), @(1200,1100,'desktop'), @(840,780,'compact'))) {
        if ($size[0] -ne 0) { $null = [OfficialRestoreUiNative]::SetWindowPos($handle, [IntPtr]::Zero, 40, 40, $size[0], $size[1], 0x0040) }
        Start-Sleep -Milliseconds 800
        $rect = New-Object OfficialRestoreUiNative+RECT
        $null = [OfficialRestoreUiNative]::GetWindowRect($handle, [ref]$rect)
        $bitmap = New-Object Drawing.Bitmap (($rect.Right-$rect.Left), ($rect.Bottom-$rect.Top))
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { $printed = [OfficialRestoreUiNative]::PrintWindow($handle, $dc, 2) } finally { $graphics.ReleaseHdc($dc) }
        $path = Join-Path $task ("ui-$($size[2]).png")
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        if ($size[2] -eq 'default') {
            $controls = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($id in @('OfficialBrowse', 'HomeBrowse', 'AgentsBrowse', 'Closed', 'Restore')) {
                $control = $controls | Where-Object { $_.Current.AutomationId -eq $id } | Select-Object -First 1
                if (-not $control -or $control.Current.IsOffscreen) { throw "Default window control is offscreen: $id" }
                if ($id -like '*Browse') {
                    $bounds = $control.Current.BoundingRectangle
                    $cropRect = New-Object Drawing.Rectangle ([int]($bounds.X-$rect.Left)), ([int]($bounds.Y-$rect.Top)), ([int]$bounds.Width), ([int]$bounds.Height)
                    $crop = $bitmap.Clone($cropRect, $bitmap.PixelFormat)
                    try {
                        $colored = 0
                        for ($y = 0; $y -lt $crop.Height; $y++) { for ($x = 0; $x -lt $crop.Width; $x++) { $pixel = $crop.GetPixel($x,$y); if ([Math]::Abs([int]$pixel.R-[int]$pixel.B) -gt 35 -and $pixel.A -gt 200) { $colored++ } } }
                        if ($colored -lt 20) { throw "Shell folder icon has no colored pixels: $id ($colored)" }
                        $crop.Save((Join-Path $task "$id.png"), [Drawing.Imaging.ImageFormat]::Png)
                        Write-Output "FOLDER_ICON_PASS $id coloredPixels=$colored"
                    } finally { $crop.Dispose() }
                }
            }
            $group = $controls | Where-Object { $_.Current.Name -eq '会话、存储与附件' } | Select-Object -First 1
            if (-not $group -or $group.Current.IsOffscreen) { throw 'Restore groups are offscreen in the default window.' }
            Write-Output "DEFAULT_LAYOUT_PASS size=$($bitmap.Width)x$($bitmap.Height) groups/closed/restore visible"
        }
        $graphics.Dispose(); $bitmap.Dispose()
        Write-Output "SCREENSHOT $path PrintWindow=$printed"
    }
    $scroll = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.GetSupportedPatterns().ProgrammaticName -contains 'ScrollPatternIdentifiers.Pattern' } | Select-Object -First 1
    if ($scroll) { $pattern = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern); $pattern.SetScrollPercent(-1, 55); Start-Sleep -Milliseconds 500 }
    $rect = New-Object OfficialRestoreUiNative+RECT
    $null = [OfficialRestoreUiNative]::GetWindowRect($handle, [ref]$rect)
    $bitmap = New-Object Drawing.Bitmap (($rect.Right-$rect.Left), ($rect.Bottom-$rect.Top))
    $graphics = [Drawing.Graphics]::FromImage($bitmap); $dc = $graphics.GetHdc()
    try { $null = [OfficialRestoreUiNative]::PrintWindow($handle, $dc, 2) } finally { $graphics.ReleaseHdc($dc) }
    $bitmap.Save((Join-Path $task 'ui-compact-scrolled.png'), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    $names | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $task 'ui-accessibility.json') -Encoding utf8
    Write-Output "UI_PASS title=$($element.Current.Name) process=$($process.Id)"
} finally {
    if (-not $process.HasExited) { $null = $process.CloseMainWindow(); $null = $process.WaitForExit(10000) }
    if (-not $process.HasExited) { throw 'The helper did not close normally.' }
    $process.Dispose()
}
