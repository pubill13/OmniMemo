param([Parameter(Mandatory=$true)][string]$Executable,[string]$ArtifactsDirectory='artifacts/verification')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase,System.Windows.Forms,System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class HotkeySmokeNative {
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct INFO { public int Size; public RECT Monitor,Work; public uint Flags; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr h,ref INFO info);
}
"@
$directory=Join-Path ([IO.Path]::GetFullPath($ArtifactsDirectory)) ('mixed-layout-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$previous=$env:OMNIMEMO_DATA_DIR
$env:OMNIMEMO_DATA_DIR=Join-Path $directory 'data'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$process=$null
$cursor=New-Object HotkeySmokeNative+POINT
[void][HotkeySmokeNative]::GetCursorPos([ref]$cursor)
$log=New-Object 'System.Collections.Generic.List[string]'
$helper=New-Object System.Windows.Forms.Form
$helper.Text='OmniMemo isolated external hotkey test'
$helper.Width=240; $helper.Height=100; $helper.ShowInTaskbar=$false
function Condition($p,$v) { New-Object System.Windows.Automation.PropertyCondition $p,$v }
function Named($root,[string]$name) { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) $name)) }
function Roots { [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,(Condition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty) $process.Id)) }
function Wait-For([scriptblock]$probe,[string]$description) {
 $end=[DateTime]::UtcNow.AddSeconds(15)
 do { [System.Windows.Forms.Application]::DoEvents(); $result=& $probe; if($result) { return $result }; Start-Sleep -Milliseconds 150 } while([DateTime]::UtcNow -lt $end)
 throw ('Timed out: '+$description)
}
function Check([bool]$value,[string]$description) { if(-not $value) { throw $description }; $log.Add('PASS: '+$description); Write-Output ('PASS: '+$description) }
function Invoke-Element($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Rect($window) { $r=New-Object HotkeySmokeNative+RECT; if(-not [HotkeySmokeNative]::GetWindowRect([IntPtr]$window.Current.NativeWindowHandle,[ref]$r)) { throw 'GetWindowRect failed' }; $r }
function Notes { foreach($w in (Roots)) { if((Named $w '메모 내용') -or (Named $w '접힌 메모, 클릭하여 펼치기')) { $w } } }
function Panel { foreach($w in (Roots)) { if($w.Current.Name -eq 'OmniMemo · 정렬 옵션' -and -not $w.Current.IsOffscreen) { return $w } } }
function Is-Tile($w) { $r=Rect $w; $scale=[HotkeySmokeNative]::GetDpiForWindow([IntPtr]$w.Current.NativeWindowHandle)/96.0; return [Math]::Abs($r.Right-$r.Left-36*$scale) -le 2 }
function Set-Text($root,[string]$name,[string]$text) { (Named $root $name).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
function Capture-Window($window,[string]$name) {
 $r=Rect $window
 $bitmap=New-Object Drawing.Bitmap ($r.Right-$r.Left),($r.Bottom-$r.Top)
 $graphics=[Drawing.Graphics]::FromImage($bitmap); $dc=$graphics.GetHdc()
 try { if(-not [HotkeySmokeNative]::PrintWindow([IntPtr]$window.Current.NativeWindowHandle,$dc,2)) { throw 'PrintWindow failed' } }
 finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
 try { $bitmap.Save((Join-Path $directory $name),[Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
}
function Picker { foreach($w in (Roots)) { if($w.Current.Name -eq '시작 위치 선택') { return $w } } }function Config { Get-Content -LiteralPath (Join-Path $env:OMNIMEMO_DATA_DIR 'layout.json') -Raw | ConvertFrom-Json }
try {
 $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
 $first=Wait-For { foreach($w in (Notes)) { return $w } } 'first note'
 Set-Text $first '메모 내용' 'Z first'
 Invoke-Element (Named $first '새 메모')
 $second=Wait-For { foreach($w in (Notes)) { if($w.Current.NativeWindowHandle -ne $first.Current.NativeWindowHandle) { return $w } } } 'second note'
 Set-Text $second '메모 내용' 'A second'
 Invoke-Element (Named $second '작은 타일로 접기')
 Invoke-Element (Named $first '메모 설정')
 $menu=Wait-For { foreach($w in (Roots)) { $item=Named $w '정렬 옵션…'; if($item) { return $item } } } 'layout menu'
 Invoke-Element $menu
 $panel=Wait-For { Panel } 'layout panel'
 Set-Text $panel '시작 X' '200'; Set-Text $panel '시작 Y' '220'
 Set-Text $panel '간격' '12'; Set-Text $panel '열 수' '2'
 $before=Rect $first
 Invoke-Element (Named $panel '지금 정렬')
 Start-Sleep -Milliseconds 600
 $after=Rect $first
 Check ($before.Left -eq $after.Left -and $before.Top -eq $after.Top -and -not (Is-Tile $first)) 'Collapsed-only arrangement leaves expanded note untouched'
 $target=$panel.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::ComboBox))) | Where-Object { $_.Current.Name -eq '정렬 대상' } | Select-Object -First 1
 $target.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 $option=Wait-For { foreach($w in (Roots)) { foreach($item in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) '펼친 메모 포함'))) { $pattern=$null; if(-not $item.Current.IsOffscreen -and $item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)) { return $item } } } } 'expanded inclusion option'
 $option.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Invoke-Element (Named $panel '지금 정렬')
 Start-Sleep -Milliseconds 700
 $after=Rect $first
 $second=Wait-For { foreach($w in (Notes)) { if($w.Current.Name -eq 'A second') { return $w } } } 'other tile'
 $other=Rect $second
 Check (-not (Is-Tile $first) -and (Is-Tile $second) -and ($after.Right-$after.Left) -eq ($before.Right-$before.Left) -and ($after.Bottom-$after.Top) -eq ($before.Bottom-$before.Top)) 'Include-expanded retains both states and expanded dimensions'
 Check ($other.Left -ge $after.Right -or $other.Top -ge $after.Bottom -or $other.Right -le $after.Left -or $other.Bottom -le $after.Top) 'Mixed-size arrangement does not overlap'
 Check (@((Config).Monitors.PSObject.Properties | Where-Object { $_.Value.IncludeExpanded }).Count -gt 0) 'Include-expanded selection persisted'
 Capture-Window $panel 'arrangement-target.png'
} catch { $log.Add('FAIL: '+$_.Exception.Message); throw }
finally {
 if($process) { if(-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
 [void][HotkeySmokeNative]::SetCursorPos($cursor.X,$cursor.Y)
 $helper.Close(); $helper.Dispose()
 $env:OMNIMEMO_DATA_DIR=$previous
 $log | Set-Content -LiteralPath (Join-Path $directory 'results.txt') -Encoding UTF8
 Write-Output ('Mixed layout artifacts: '+$directory)
}

