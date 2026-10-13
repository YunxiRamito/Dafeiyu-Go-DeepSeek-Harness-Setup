param(
    [Parameter(Mandatory=$true)][string]$InstallerDirectory,
    [Parameter(Mandatory=$true)][string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MigrationSmokeNative {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
$work = [IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Force -Path $work, "$work/settings", "$work/temp", "$work/source/profiles/desktop" | Out-Null
$env:TEMP = "$work/temp"
$env:TMP = $env:TEMP
$env:DAFEIYU_INSTALLER_SETTINGS_DIRECTORY = "$work/settings"
$env:DSH_HOME = "$work/source"
$env:DOTNET_ROOT = 'G:/DeepSeek DSH/.tools/dotnet'
$env:__COMPAT_LAYER = 'RunAsInvoker'
$process = Start-Process -FilePath (Join-Path $InstallerDirectory 'DSH-Installer.exe') -ArgumentList '--page=3','--dry-run','--lang=zh' -PassThru
$window = $null
$checks = [Collections.Generic.List[string]]::new()
function Wait-For([scriptblock]$Test, [int]$Seconds = 45) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Installer exited: $($process.ExitCode)" }
        $result = & $Test
        if ($result) { return $result }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Timed out waiting for installer UI'
}
function Find-Id([string]$Id) {
    $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
}
function Find-Name([string]$Name) {
    $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name))
}
function Invoke-Button($Element) { $Element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Set-Value($Element, [string]$Value) { $Element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Value) }
function Select-Radio($Element) { $Element.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function Check([bool]$Condition, [string]$Description) {
    if (!$Condition) { throw "FAIL $Description" }
    $checks.Add("PASS $Description")
    Write-Output "PASS $Description"
}
function Capture([string]$Name) {
    Start-Sleep -Milliseconds 650
    $panes = $window.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty,$true))
    foreach ($pane in $panes) {
        $scroll = $pane.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        if ($scroll.Current.VerticallyScrollable) { $scroll.SetScrollPercent(-1,0) }
    }
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][MigrationSmokeNative]::ShowWindow($handle, 9)
    [void][MigrationSmokeNative]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 400
    if ([MigrationSmokeNative]::GetForegroundWindow() -ne $handle) { throw 'Installer is not foreground; refusing screenshot' }
    $rect = Wait-For {
        $bounds = [MigrationSmokeNative+RECT]::new()
        if ([MigrationSmokeNative]::GetWindowRect($handle, [ref]$bounds) -and
            $bounds.Right - $bounds.Left -ge 320 -and $bounds.Bottom - $bounds.Top -ge 240) { $bounds }
    } -Seconds 10
    Write-Output "Capturing $Name at $($rect.Left),$($rect.Top) size $($rect.Right-$rect.Left)x$($rect.Bottom-$rect.Top)"
    $bitmap = [Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size)
        $bitmap.Save((Join-Path $work "$Name.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
    $window = Wait-For {
        [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))
    }
    Write-Output 'UI window located; waiting for detection'
    $continue = Wait-For { $button = Find-Name '继续'; if ($button -and $button.Current.IsEnabled) { $button } }
    Invoke-Button $continue
    $migrate = Wait-For { Find-Id 'MigrateButton' }
    Check (!(Find-Name '下一步').Current.IsEnabled) 'official installation requires an explicit action'
    $description = Find-Id 'ExistingDescription'
    Check ($description.Current.Name.Contains('暂不适配直接使用官方版')) 'official app is identified as migration-only'
    $combo = Find-Id 'InstallationBox'
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $official = Wait-For {
        $window.FindAll([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)) |
            Where-Object { $_.Current.Name.StartsWith('官方版') } | Select-Object -First 1
    }
    $official.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $migrate = Wait-For { $element = Find-Id 'MigrateButton'; if ($element -and $element.Current.IsEnabled) { $element } }
    Select-Radio $migrate
    $migrationOptions = Wait-For { $element = Find-Id 'MigrationOptions'; if ($element -and !$element.Current.IsOffscreen) { $element } }
    $migrationOptions.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $source = Wait-For { Find-Id 'MigrationHomeBox' }
    Check ($source.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq "$work/source") 'migration uses captured DSH_HOME'
    (Find-Id 'MigrationOptions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    $target = Join-Path $work 'new-dsh'
    Set-Value (Find-Id 'PathBox') $target
    Capture 'migration-choice'
    Invoke-Button (Find-Name '下一步')
    $null = Wait-For { Find-Id 'RootBox' }
    Invoke-Button (Find-Name '上一步')
    $null = Wait-For { Find-Id 'MigrationOptions' }
    Check ((Find-Id 'PathBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $target) 'back navigation preserves installation target'
    Check ((Find-Id 'MigrateButton').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) 'back navigation preserves migration choice'
    Select-Radio (Find-Id 'FreshButton')
    $null = Wait-For { $element = Find-Id 'MigrationOptions'; !$element -or $element.Current.IsOffscreen }
    Check ((Find-Id 'MigrationHomeBox') -eq $null -or (Find-Id 'MigrationHomeBox').Current.IsOffscreen) 'fresh installation hides the migration source'
    Capture 'fresh-choice'
    Check (!(Test-Path -LiteralPath $target)) 'dry-run navigation does not create the installation target'
} finally {
    if ($window) {
        $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
            ForEach-Object { "{0} | {1} | {2}" -f $_.Current.AutomationId, $_.Current.ControlType.ProgrammaticName, $_.Current.Name } |
            Set-Content -LiteralPath (Join-Path $work 'ui-tree.txt') -Encoding utf8
    }
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'checks.json') -Encoding utf8
    $process.Refresh()
    if (!$process.HasExited) {
        if ($window) { [void][MigrationSmokeNative]::PostMessage([IntPtr]$window.Current.NativeWindowHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) }
        if (!$process.WaitForExit(5000)) { Stop-Process -Id $process.Id }
    }
}
