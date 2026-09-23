param([Parameter(Mandatory=$true)][string]$Executable, [string]$ArtifactsDirectory='artifacts/verification')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TrayMouse {
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
$directory=Join-Path ([IO.Path]::GetFullPath($ArtifactsDirectory)) ('tray-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$previous=$env:OMNIMEMO_DATA_DIR
$env:OMNIMEMO_DATA_DIR=Join-Path $directory 'data'
$process=$null
$log=New-Object 'System.Collections.Generic.List[string]'
$original=New-Object TrayMouse+POINT
[void][TrayMouse]::GetCursorPos([ref]$original)
function Wait-Element([scriptblock]$query,[string]$description) {
 $end=[DateTime]::UtcNow.AddSeconds(15)
 do { $value=& $query; if ($value) { return $value }; Start-Sleep -Milliseconds 200 } while ([DateTime]::UtcNow -lt $end)
 throw "Timed out: $description"
}
function Condition($property,$value) { New-Object System.Windows.Automation.PropertyCondition $property,$value }
function Named($root,[string]$name) { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) $name)) }
function Taskbar { [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,(Condition ([System.Windows.Automation.AutomationElement]::ClassNameProperty) 'Shell_TrayWnd')) }
function Invoke-Element($element) {
 $pattern=$null
 if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)) { $pattern.Invoke(); return }
 if ($element.TryGetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern,[ref]$pattern)) { $pattern.DoDefaultAction(); return }
 throw ('Element cannot be invoked: '+$element.Current.Name)
}
function Clickable-Bounds($element) {
 try {
  $current=$element.Current
  $bounds=$current.BoundingRectangle
  if ($current.IsOffscreen -or -not $current.IsEnabled -or $bounds.IsEmpty -or $bounds.Width -le 0 -or $bounds.Height -le 0) { return $null }
  foreach ($number in @($bounds.X,$bounds.Y,$bounds.Width,$bounds.Height)) {
   if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { return $null }
  }
  return $bounds
 } catch [System.Windows.Automation.ElementNotAvailableException] { return $null }
}
function Find-ClickableOmniIcon {
 # FindAll avoids selecting the first stale, empty shell placeholder after a prior run.
 $icons=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) 'OmniMemo · 개인용 메모'))
 $visible=@(foreach ($candidate in $icons) { if ($null -ne (Clickable-Bounds $candidate)) { $candidate } })
 if ($visible.Count -gt 1) { throw 'Multiple clickable OmniMemo tray icons; cannot safely choose the test icon.' }
 if ($visible.Count -eq 1) { return $visible[0] }
}
function Open-TrayMenu {
 $icon=Find-ClickableOmniIcon
 if (-not $icon) {
  $bar=Taskbar
  if ($bar) {
   $overflow=Named $bar '숨겨진 아이콘 표시'
   if ($overflow -and $null -ne (Clickable-Bounds $overflow)) { Invoke-Element $overflow }
  }
 }
 # Shell notification-area layout can lag behind showing the overflow. Reacquire
 # the exact named icon each time; never retain a stale element or guess a point.
 $target=Wait-Element {
  $candidate=Find-ClickableOmniIcon
  if ($candidate) {
   $rectangle=Clickable-Bounds $candidate
   if ($null -ne $rectangle) { return @{ Icon=$candidate; Bounds=$rectangle } }
  }
 } 'visible OmniMemo notification icon with nonempty bounds'
 $bounds=$target.Bounds
 [void][TrayMouse]::SetCursorPos([int]($bounds.X+$bounds.Width/2),[int]($bounds.Y+$bounds.Height/2))
 [TrayMouse]::mouse_event(8,0,0,0,[UIntPtr]::Zero)
 [TrayMouse]::mouse_event(16,0,0,0,[UIntPtr]::Zero)
 Wait-Element {
  $roots=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,(Condition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty) $process.Id))
  foreach ($root in $roots) {
   $items=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::MenuItem)))
   foreach ($item in $items) { if ($item.Current.Name -eq '종료') { return $root } }
  }
 } 'right-click context menu'
}
function Record([string]$text) { $log.Add('PASS: '+$text); Write-Output ('PASS: '+$text) }
function Window-Rect($window) {
 $rect=New-Object TrayMouse+RECT
 if (-not [TrayMouse]::GetWindowRect([IntPtr]$window.Current.NativeWindowHandle,[ref]$rect)) { throw 'Window rectangle unavailable.' }
 $rect
}
function Drag-Window($window,[int]$offsetX,[int]$offsetY,[int]$targetX,[int]$targetY) {
 [void][TrayMouse]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)
 Start-Sleep -Milliseconds 200
 $rect=Window-Rect $window
 [void][TrayMouse]::SetCursorPos($rect.Left+$offsetX,$rect.Top+$offsetY)
 Start-Sleep -Milliseconds 100
 $hit=New-Object TrayMouse+POINT; $hit.X=$rect.Left+$offsetX; $hit.Y=$rect.Top+$offsetY
 if ([TrayMouse]::WindowFromPoint($hit) -ne [IntPtr]$window.Current.NativeWindowHandle) { throw "Drag target is obscured by another window." }
 [TrayMouse]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 100
 try {
  [void][TrayMouse]::SetCursorPos($rect.Left+$offsetX+5,$rect.Top+$offsetY)
  Start-Sleep -Milliseconds 100
  for($step=1;$step -le 100;$step++) {
   $x=[int]($rect.Left+($targetX-$rect.Left)*$step/100)+$offsetX
   $y=[int]($rect.Top+($targetY-$rect.Top)*$step/100)+$offsetY
   [void][TrayMouse]::SetCursorPos($x,$y)
   Start-Sleep -Milliseconds 10
  }
 } finally { [TrayMouse]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
 Start-Sleep -Milliseconds 300
}
try {
 $exe=(Resolve-Path -LiteralPath $Executable).Path
 $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
 Start-Sleep -Seconds 3
 $menu=Open-TrayMenu
 foreach ($name in @('새 메모','메모 목록','전체 숨기기','전체 보이기','설정','종료')) {
  if (-not (Named $menu $name)) { throw ('Missing tray action: '+$name) }
 }
 Record 'OmniMemo notification icon opens all six right-click actions'
 Invoke-Element (Named $menu '메모 목록')
 $null=Wait-Element { Named ([System.Windows.Automation.AutomationElement]::RootElement) '메모 내용 검색' } 'list search opened from tray'
 Record 'Tray list action opens note list'
 $menu=Open-TrayMenu
 Invoke-Element (Named $menu '설정')
 $null=Wait-Element { Named ([System.Windows.Automation.AutomationElement]::RootElement) 'Windows 로그인 시 OmniMemo 실행' } 'settings opened from tray'
 Record 'Tray settings action opens settings'
 $menu=Open-TrayMenu
 Invoke-Element (Named $menu '새 메모')
 Start-Sleep -Seconds 1
 $roots=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,(Condition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty) $process.Id))
 $noteWindows=@(foreach ($root in $roots) { if (Named $root '메모 내용') { $root } })
 if ($noteWindows.Count -lt 2) { throw 'New note action did not create another note.' }
 Record 'Tray new-note action creates second note'
 # The freshly created note is foreground; drag only a point on its own title bar.
 $moving=$noteWindows[0]; $anchor=$noteWindows[1]
 $movingRect=Window-Rect $moving; $anchorRect=Window-Rect $anchor
 if ($movingRect.Left -lt $anchorRect.Left) { $swap=$moving; $moving=$anchor; $anchor=$swap; $anchorRect=Window-Rect $anchor }
 Drag-Window $moving 170 14 ($anchorRect.Right+6) $anchorRect.Top
 $snapped=Window-Rect $moving
 if ([Math]::Abs($snapped.Left-$anchorRect.Right) -gt 2 -or [Math]::Abs($snapped.Top-$anchorRect.Top) -gt 2) { throw ("Actual title-bar drag did not snap: actual={0},{1}; target={2},{3}" -f $snapped.Left,$snapped.Top,$anchorRect.Right,$anchorRect.Top) }
 Record 'Actual mouse drag snaps expanded note beside another note'
 Invoke-Element (Named $moving '작은 타일로 접기')
 Start-Sleep -Milliseconds 300
 Drag-Window $moving 18 18 $anchorRect.Left ($anchorRect.Bottom+6)
 $snapped=Window-Rect $moving
 if ([Math]::Abs($snapped.Top-$anchorRect.Bottom) -gt 2 -or [Math]::Abs($snapped.Left-$anchorRect.Left) -gt 2 -or ($snapped.Right-$snapped.Left) -gt 72) { throw ("Tile drag failed: actual={0},{1},{2},{3}; target={4},{5}" -f $snapped.Left,$snapped.Top,$snapped.Right,$snapped.Bottom,$anchorRect.Left,$anchorRect.Bottom) }
 Record 'Actual mouse drag keeps tile collapsed and snaps below peer'
 [void][TrayMouse]::SetCursorPos($snapped.Left+18,$snapped.Top+18)
 Start-Sleep -Milliseconds 100
 [TrayMouse]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 100
 [TrayMouse]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 $null=Wait-Element { if ((Window-Rect $moving).Right-(Window-Rect $moving).Left -gt 100) { return $true } } 'physical tile click expands after drag'
 Record 'Physical tile click expands after drag'
 $buttons=(Taskbar).FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Button)))
 foreach ($button in $buttons) {
  if ($button.Current.AutomationId -like 'Appid:*OmniMemo*') { throw 'OmniMemo has a taskbar application button.' }
 }
 Record 'No OmniMemo taskbar application button while note/list/settings are open'
 $menu=Open-TrayMenu
 Invoke-Element (Named $menu '종료')
 if (-not $process.WaitForExit(10000)) { throw 'Tray exit did not terminate application.' }
 Record 'Tray exit saves and terminates the process'
} catch { $log.Add('FAIL: '+$_.Exception.Message); throw }
finally {
 if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
 [void][TrayMouse]::SetCursorPos($original.X,$original.Y)
 $env:OMNIMEMO_DATA_DIR=$previous
 $log | Set-Content -LiteralPath (Join-Path $directory 'results.txt') -Encoding UTF8
 Write-Output ('Tray artifacts: '+$directory)
}
