param([Parameter(Mandatory=$true)][string]$Executable,[string]$ArtifactsDirectory='artifacts/verification')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LayoutNative {
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int ht,uint f);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint data,UIntPtr extra);
}
"@
$directory=Join-Path ([IO.Path]::GetFullPath($ArtifactsDirectory)) ('layout-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$previous=$env:OMNIMEMO_DATA_DIR
$env:OMNIMEMO_DATA_DIR=Join-Path $directory 'data'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$process=$null
$originalCursor=New-Object LayoutNative+POINT
[void][LayoutNative]::GetCursorPos([ref]$originalCursor)
$log=New-Object 'System.Collections.Generic.List[string]'
function Condition($p,$v) { New-Object System.Windows.Automation.PropertyCondition $p,$v }
function Named($root,[string]$name) { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) $name)) }
function Own-Windows { [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,(Condition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty) $process.Id)) }
function Own-Named([string]$name) { foreach($root in (Own-Windows)) { $element=Named $root $name; if($element -and -not $element.Current.IsOffscreen) { return $element } } }
function Wait-For([scriptblock]$probe,[string]$description) {
 $end=[DateTime]::UtcNow.AddSeconds(15)
 do { $result=& $probe; if($result) { return $result }; Start-Sleep -Milliseconds 150 } while([DateTime]::UtcNow -lt $end)
 throw ('Timed out: '+$description)
}
function Check([bool]$value,[string]$description) { if(-not $value) { throw $description }; $log.Add('PASS: '+$description); Write-Output ('PASS: '+$description) }
function Invoke-Element($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Rect($window) { $r=New-Object LayoutNative+RECT; if(-not [LayoutNative]::GetWindowRect([IntPtr]$window.Current.NativeWindowHandle,[ref]$r)) { throw 'GetWindowRect failed' }; $r }
function Notes { foreach($root in (Own-Windows)) { if((Named $root '메모 내용') -or (Named $root '접힌 메모, 클릭하여 펼치기')) { $root } } }
function Note-Named([string]$name) { foreach($root in (Notes)) { if($root.Current.Name -eq $name) { return $root } } }
function Set-Text($window,[string]$text) { (Named $window '메모 내용').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
function Tile-Menu($window) {
 $handle=[IntPtr]$window.Current.NativeWindowHandle
 [void][LayoutNative]::SetForegroundWindow($handle)
 Start-Sleep -Milliseconds 200
 $bounds=Rect $window
 $point=New-Object LayoutNative+POINT
 $point.X=[int](($bounds.Left+$bounds.Right)/2)
 $point.Y=[int](($bounds.Top+$bounds.Bottom)/2)
 if([LayoutNative]::WindowFromPoint($point) -ne $handle) { throw 'Owned tile is obscured; cannot safely right-click.' }
 [void][LayoutNative]::SetCursorPos($point.X,$point.Y)
 [LayoutNative]::mouse_event(8,0,0,0,[UIntPtr]::Zero)
 [LayoutNative]::mouse_event(16,0,0,0,[UIntPtr]::Zero)
 $null=Wait-For { Own-Named '접힌 메모 정렬' } 'tile arrangement context menu'
 $item=Own-Named '접힌 메모 정렬'
 $item.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
}
function Select-Arrange($window,[string]$choice) { Tile-Menu $window; $item=Wait-For { Own-Named $choice } $choice; Invoke-Element $item; Start-Sleep -Milliseconds 700 }
function Set-Color($window,[string]$color) {
 Invoke-Element (Named $window '메모 설정')
 $menu=Wait-For { Own-Named '메모 색상' } 'color menu'
 $menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 Invoke-Element (Wait-For { Own-Named $color } $color)
 Start-Sleep -Milliseconds 300
}
function Start-Owned { $script:process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru }
try {
 Start-Owned
 $first=Wait-For { foreach($w in (Notes)) { return $w } } 'first note'
 Set-Text $first 'Layout first blue'
 $handle=[IntPtr]$first.Current.NativeWindowHandle
 $scale=[LayoutNative]::GetDpiForWindow($handle)/96.0
 [void][LayoutNative]::SetWindowPos($handle,[IntPtr]::Zero,[int](240*$scale),[int](200*$scale),0,0,0x0015)
 Start-Sleep -Milliseconds 300
 $source=Rect $first
 Set-Color $first '파랑'
 Invoke-Element (Named $first '새 메모')
 $second=Wait-For { foreach($w in (Notes)) { if($w.Current.NativeWindowHandle -ne $first.Current.NativeWindowHandle) { return $w } } } 'source-relative second note'
 $created=Rect $second
 Check ([Math]::Abs($created.Left-$source.Left-28*$scale) -le 2 -and [Math]::Abs($created.Top-$source.Top-28*$scale) -le 2) 'Plus creates note 28 DIP diagonally from moved source'
 Set-Text $second 'Layout second yellow'
 Invoke-Element (Named $second '새 메모')
 $third=Wait-For { foreach($w in (Notes)) { if($w.Current.NativeWindowHandle -ne $first.Current.NativeWindowHandle -and $w.Current.NativeWindowHandle -ne $second.Current.NativeWindowHandle) { return $w } } } 'third note'
 Set-Text $third 'Layout third pink'
 Set-Color $third '분홍'
 Invoke-Element (Named $first '작은 타일로 접기')
 Invoke-Element (Named $second '작은 타일로 접기')
 Start-Sleep -Milliseconds 400
 Select-Arrange $first '생성순으로 정렬'
 $a=Rect $first; $b=Rect $second
 Check ($a.Left -lt $b.Left -and $a.Top -eq $b.Top) 'Creation order puts older blue tile before yellow'
 Select-Arrange $first '색상별로 정렬'
 $a=Rect $first; $b=Rect $second
 Check ($b.Left -lt $a.Left -and $a.Top -eq $b.Top) 'Color order moves yellow before blue'
 Select-Arrange $first '자동 정렬'
 Invoke-Element (Named $third '작은 타일로 접기')
 $null=Wait-For { $a=Rect $first; $b=Rect $second; $c=Rect $third; if($b.Left -lt $c.Left -and $c.Left -lt $a.Left -and $a.Top -eq $c.Top) { return $true } } 'automatic sorted placement after third note collapses'
 Check $true 'Automatic arrangement inserts new pink tile between yellow and blue'
 $settingsPath=Join-Path $env:OMNIMEMO_DATA_DIR 'layout.json'
 $config=Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
 Check ($config.AutoArrange -and $config.SortByColor) 'Auto arrangement and color sort are persisted'
 Start-Sleep -Seconds 3
 Stop-Process -Id $process.Id
 $process.WaitForExit(); $process.Dispose(); $process=$null
 Start-Owned
 $first=Wait-For { Note-Named 'Layout first blue' } 'blue after restart'
 $second=Wait-For { Note-Named 'Layout second yellow' } 'yellow after restart'
 $third=Wait-For { Note-Named 'Layout third pink' } 'pink after restart'
 $null=Wait-For { $a=Rect $first; $b=Rect $second; $c=Rect $third; if($b.Left -lt $c.Left -and $c.Left -lt $a.Left -and ($a.Right-$a.Left) -lt 100) { return $true } } 'restored collapsed arrangement'
 Tile-Menu $first
 $auto=Wait-For { Own-Named '자동 정렬' } 'restored auto menu'
 $toggle=$auto.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
 Check ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) 'Auto arrangement remains checked after restart'
 Check $true 'Collapsed tile color order survives restart'
} catch { $log.Add('FAIL: '+$_.Exception.Message); throw }
finally {
 if($process) { if(-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
 [void][LayoutNative]::SetCursorPos($originalCursor.X,$originalCursor.Y)
 $env:OMNIMEMO_DATA_DIR=$previous
 $log | Set-Content -LiteralPath (Join-Path $directory 'results.txt') -Encoding UTF8
 Write-Output ('Layout artifacts: '+$directory)
}
