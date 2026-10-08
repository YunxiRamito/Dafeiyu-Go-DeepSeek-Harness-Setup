param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [Parameter(Mandatory=$true)][string]$Target,
    [Parameter(Mandatory=$true)][string]$Output,
    [string]$Source,
    [switch]$Discovery,
    [switch]$CancelScan
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ImportPageCapture {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint f);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr z,int x,int y,int w,int height,uint f);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
[ImportPageCapture]::SetThreadDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$env:__COMPAT_LAYER = 'RunAsInvoker'
$arguments = @('--page=11','--lang=zh',('"--dsh-root=' + $Target + '"'))
$process = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
try {
    $null = $process.WaitForInputIdle(20000)
    $deadline = [datetime]::UtcNow.AddSeconds(20)
    do {
        $process.Refresh()
        if($process.HasExited){throw "Installer preview exited $($process.ExitCode)"}
        $window=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)) | Where-Object {$_.Current.NativeWindowHandle -ne 0} | Select-Object -First 1
        $handle=if($window){[IntPtr]::new($window.Current.NativeWindowHandle)}else{[IntPtr]::Zero}
        if($handle -eq [IntPtr]::Zero){Start-Sleep -Milliseconds 100}
    } while($handle -eq [IntPtr]::Zero -and [datetime]::UtcNow -lt $deadline)
    if($handle -eq [IntPtr]::Zero){throw 'Installer preview never opened'}
    $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
    function Visible-Nodes {
        @($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition) | Where-Object {!$_.Current.IsOffscreen})
    }
    function Find-Button([string]$Name) {
        Visible-Nodes | Where-Object {$_.Current.Name -eq $Name -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button} | Select-Object -First 1
    }
    function Assert-Name([string]$Name) {
        if(!(Visible-Nodes | Where-Object {$_.Current.Name -eq $Name})){throw "Import UI missing: $Name"}
    }
    function Capture([string]$Name) {
        $rows = foreach($node in (Visible-Nodes)) {
            [pscustomobject]@{Name=$node.Current.Name;Type=$node.Current.ControlType.ProgrammaticName;Enabled=$node.Current.IsEnabled;Bounds=$node.Current.BoundingRectangle.ToString()}
        }
        $rows | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $Output "$Name-uia.json") -Encoding utf8
        $rect = [ImportPageCapture+RECT]::new()
        [ImportPageCapture]::GetWindowRect($handle,[ref]$rect) | Out-Null
        $bitmap = [Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc=$graphics.GetHdc()
        try {
            if(![ImportPageCapture]::PrintWindow($handle,$dc,2)){throw 'Installer capture failed'}
        } finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save((Join-Path $Output "$Name.png"),[Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose(); $bitmap.Dispose()
    }
    Start-Sleep -Milliseconds 750
    foreach($width in 1200,800) {
        [ImportPageCapture]::SetWindowPos($handle,[IntPtr]::Zero,40,40,$width,760,4) | Out-Null
        Start-Sleep -Milliseconds 500
        foreach($name in @('导入原有数据','你是否之前安装过 DSH 或者 Dafeiyu-Go？若没有，请点击下一步。','从旧的客户端恢复备份。','自动查找','扫描其他磁盘','从旧的 Dafeiyu-Go 导入','从旧的 DSH 客户端导入','下一步')) {Assert-Name $name}
        if(Visible-Nodes | Where-Object {$_.Current.Name -eq '还没选。'}){throw 'Unselected path placeholder remains visible'}
        $auto=(Find-Button '自动查找').Current.BoundingRectangle
        $dym=(Find-Button '从旧的 Dafeiyu-Go 导入').Current.BoundingRectangle
        $dsh=(Find-Button '从旧的 DSH 客户端导入').Current.BoundingRectangle
        if($auto.Bottom -ge $dym.Top -or [Math]::Abs($dym.Top-$dsh.Top) -gt 1 -or [Math]::Abs($dym.Width-$dsh.Width) -gt 2){throw 'Import actions are not arranged uniformly with discovery first'}
        Capture "initial-$width"
        Write-Host "PASS initial import page at width $width; auto=$auto dym=$dym dsh=$dsh"
    }
    [ImportPageCapture]::SetWindowPos($handle,[IntPtr]::Zero,40,40,1200,760,4) | Out-Null
    if($Discovery) {
        (Find-Button '自动查找').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $deadline=[datetime]::UtcNow.AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 100
            $found=Visible-Nodes | Where-Object {$_.Current.Name -eq '选择导入来源'}
        } while(!$found -and [datetime]::UtcNow -lt $deadline)
        if(!$found){throw 'Multiple discovery results did not open source selection'}
        Assert-Name 'Dafeiyu-Go 备份 · .dym'
        Assert-Name 'DSH 客户端数据'
        Capture 'discovery-results'
        if(!$Source){$Source=Join-Path (Split-Path -Parent $Target) 'source'}
        $pathNode=Visible-Nodes | Where-Object {$_.Current.Name -eq $Source} | Select-Object -First 1
        if(!$pathNode){throw "Isolated discovery source is not shown: $Source"}
        $item=$pathNode
        while($item -and $item.Current.ControlType -ne [Windows.Automation.ControlType]::ListItem){$item=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($item)}
        if(!$item){throw 'Discovered source has no selectable list item'}
        $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        (Find-Button '选择').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $deadline=[datetime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $confirmation=Visible-Nodes | Where-Object {$_.Current.Name -eq '导入数据'}
        } while(!$confirmation -and [datetime]::UtcNow -lt $deadline)
        if(!$confirmation){throw 'Selected discovery source did not enter import confirmation'}
        if(!(Visible-Nodes | Where-Object {$_.Current.Name.StartsWith('来源：') -and $_.Current.Name.Contains($Source)})){throw 'Import confirmation does not show selected isolated source'}
        Capture 'discovered-import-confirmation'
        (Find-Button '取消').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $deadline=[datetime]::UtcNow.AddSeconds(5)
        do {
            Start-Sleep -Milliseconds 100
            $ready=Find-Button '下一步'
        } while((!$ready -or !$ready.Current.IsEnabled) -and [datetime]::UtcNow -lt $deadline)
        if(!$ready -or !$ready.Current.IsEnabled){throw 'Import confirmation did not finish closing'}
        Write-Host 'PASS discovery results show both source types and selected source enters import confirmation'
    }
    if($CancelScan) {
        (Find-Button '扫描其他磁盘').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $deadline=[datetime]::UtcNow.AddSeconds(5)
        do {
            $cancel=Find-Button '取消查找'
            if(!$cancel){Start-Sleep -Milliseconds 25}
        } while(!$cancel -and [datetime]::UtcNow -lt $deadline)
        if(!$cancel){throw 'Expanded scan has no cancel control'}
        Capture 'scanning'
        $cancel.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $deadline=[datetime]::UtcNow.AddSeconds(10)
        do {
            Start-Sleep -Milliseconds 100
            $canceled=Visible-Nodes | Where-Object {$_.Current.Name -eq '已取消查找。'}
        } while(!$canceled -and [datetime]::UtcNow -lt $deadline)
        if(!$canceled -or !(Find-Button '自动查找').Current.IsEnabled -or !(Find-Button '下一步').Current.IsEnabled){throw 'Canceled scan did not restore navigation/actions'}
        Capture 'scan-canceled'
        Write-Host 'PASS expanded scan cancellation restores import actions and navigation'
    }
} finally {
    if(!$process.HasExited){$process.CloseMainWindow() | Out-Null; if(!$process.WaitForExit(5000)){Stop-Process -Id $process.Id -Force}}
}
