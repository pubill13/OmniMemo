param([Parameter(Mandatory=$true)][string]$Executable,[string]$ArtifactsDirectory='artifacts/verification',[switch]$ShowOverlay)
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
$directory=Join-Path ([IO.Path]::GetFullPath($ArtifactsDirectory)) ('input-drag-'+[Guid]::NewGuid().ToString('N'))
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
function Drag-Note($w, [int]$localX, [int]$localY) {
 $r=Rect $w; $scale=[HotkeySmokeNative]::GetDpiForWindow([IntPtr]$w.Current.NativeWindowHandle)/96.0
 [void][HotkeySmokeNative]::SetForegroundWindow([IntPtr]$w.Current.NativeWindowHandle)
 Start-Sleep -Milliseconds 200
 if([HotkeySmokeNative]::GetForegroundWindow() -ne [IntPtr]$w.Current.NativeWindowHandle) { throw 'Cannot focus isolated note for mouse test' }
 $sx=[int]($r.Left+$localX*$scale); $sy=[int]($r.Top+$localY*$scale)
 [void][HotkeySmokeNative]::SetCursorPos($sx,$sy)
 [HotkeySmokeNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 try {
  Start-Sleep -Milliseconds 150
  for($i=1;$i -le 10;$i++) { [void][HotkeySmokeNative]::SetCursorPos($sx+10*$i,$sy+6*$i); Start-Sleep -Milliseconds 40 }
  $during=Rect $w
 } finally { [HotkeySmokeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
 Start-Sleep -Milliseconds 300
 $after=Rect $w
 Write-Output "Drag ($localX,$localY): before=$($r.Left),$($r.Top) during=$($during.Left),$($during.Top) after=$($after.Left),$($after.Top)"
 Check ([Math]::Abs($during.Left-$r.Left-100) -le 15 -and [Math]::Abs($during.Top-$r.Top-60) -le 15) 'Dragging moves note with cursor'
 Check ($after.Left -eq $during.Left -and $after.Top -eq $during.Top) 'Released expanded note stays at manually dragged position'
}
Add-Type -ReferencedAssemblies @([System.Windows.Automation.AutomationElement].Assembly.Location,[System.Windows.Automation.AutomationProperty].Assembly.Location) @"
using System;
using System.Threading;
using System.Windows.Automation;
public static class EnabledRecorder {
 public static int Disabled;
 private static AutomationPropertyChangedEventHandler handler = (s,e) => { if(e.Property==AutomationElement.IsEnabledProperty && Equals(e.NewValue,false)) Interlocked.Increment(ref Disabled); };
 public static void Start(AutomationElement element) { Disabled=0; Automation.AddAutomationPropertyChangedEventHandler(element,TreeScope.Element,handler,AutomationElement.IsEnabledProperty); }
 public static void Stop(AutomationElement element) { Automation.RemoveAutomationPropertyChangedEventHandler(element,handler); }
}
"@
function Send-Chord([byte]$key,[bool]$global=$true) {
 if($global) {
  $helper.Show(); $helper.Activate(); [void][HotkeySmokeNative]::SetForegroundWindow($helper.Handle)
  [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 200
  if([HotkeySmokeNative]::GetForegroundWindow() -ne $helper.Handle) { throw 'Cannot focus isolated external helper; refusing keyboard input.' }
 }
 $keys=if($global) { @(0x11,0x12,0x10) } else { @(0x11,0x10) }
 try { foreach($k in $keys) { [HotkeySmokeNative]::keybd_event([byte]$k,0,0,[UIntPtr]::Zero) }; [HotkeySmokeNative]::keybd_event($key,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60 }
 finally { [HotkeySmokeNative]::keybd_event($key,0,2,[UIntPtr]::Zero); [array]::Reverse($keys); foreach($k in $keys) { [HotkeySmokeNative]::keybd_event([byte]$k,0,2,[UIntPtr]::Zero) } }
 Start-Sleep -Milliseconds 650
}
function Root-Named([string]$name) { foreach($w in (Roots)) { if($w.Current.Name -eq $name) { return $w } } }
function Delete-Note($note) {
 Invoke-Element (Named $note '메모 설정')
 $action=Wait-For { foreach($w in (Roots)) { $item=Named $w '휴지통으로 이동'; if($item -and -not $item.Current.IsOffscreen){ return $item } } } 'delete menu'
 Invoke-Element $action
}
try {
 New-Item -ItemType Directory -Path $env:OMNIMEMO_DATA_DIR | Out-Null
 @{OverlayVisible=$true; Version=4; OverlayOpacity=1.0} | ConvertTo-Json | Set-Content (Join-Path $env:OMNIMEMO_DATA_DIR 'layout.json') -Encoding UTF8
 $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
 $first=Wait-For { foreach($w in (Notes)) { return $w } } 'first note'
 $r=Rect $first; $scale=[HotkeySmokeNative]::GetDpiForWindow([IntPtr]$first.Current.NativeWindowHandle)/96.0
 $info=New-Object HotkeySmokeNative+INFO; $info.Size=[Runtime.InteropServices.Marshal]::SizeOf($info)
 [void][HotkeySmokeNative]::GetMonitorInfo([HotkeySmokeNative]::MonitorFromWindow([IntPtr]$first.Current.NativeWindowHandle,2),[ref]$info)
 Check ([Math]::Abs($r.Right-($info.Work.Right-8*$scale)) -le 2 -and [Math]::Abs($r.Top-($info.Work.Top+8*$scale)) -le 2) 'New external note starts at configured top-right corner'
 Set-Text $first '메모 내용' '복원할 최신 본문'
 Start-Sleep -Milliseconds 800
 $overlay=Wait-For { Root-Named 'OmniMemo · 데스크톱 패널' } 'panel'
 (Named $overlay '색상별 조작').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 Check ($null -ne (Named $overlay '노랑 · 보이는 메모 1개')) 'Color chip counts visible yellow note'
 Capture-Window $overlay 'panel-chips.png'
 Invoke-Element (Named $overlay '설정')
 $settings=Wait-For { Root-Named 'OmniMemo · 설정' } 'settings'
 Check ($null -eq (Named $settings '접어서 정돈')) 'Settings has no arrangement action'
 Check ($null -ne (Named $settings '적용')) 'Settings retains Apply'
 Capture-Window $settings 'settings-preview.png'
 Invoke-Element (Named $settings '적용')
 $after=Rect $first
 Check ($after.Left -eq $r.Left -and $after.Top -eq $r.Top) 'Apply does not move notes'
 $settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Delete-Note $first
 $undo=Wait-For { Named $overlay '삭제 실행 취소' } 'inline undo'
 Check (@(Notes).Count -eq 0) 'Deleted note is hidden'
 Invoke-Element $undo
 $restored=Wait-For { foreach($w in (Notes)) { return $w } } 'restored note'
 Check ((Named $restored '메모 내용').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '복원할 최신 본문') 'Undo restores original note text'
 $rr=Rect $restored
 Check ($rr.Left -eq $r.Left -and $rr.Top -eq $r.Top) 'Undo restores original position'
 Send-Chord 0x50
 Delete-Note $restored
 $toast=Wait-For { Root-Named 'OmniMemo · 삭제 실행 취소' } 'nonactivating toast'
 Check ([HotkeySmokeNative]::GetForegroundWindow() -ne [IntPtr]$toast.Current.NativeWindowHandle) 'Toast does not activate itself'
 Invoke-Element (Named $toast '삭제 실행 취소')
 $restored=Wait-For { foreach($w in (Notes)) { return $w } } 'toast-restored note'
 Invoke-Element (Named $restored '새 메모')
 [void](Wait-For { if(@(Notes).Count -eq 2){ return $true } } 'new source note')
 Check (@(Notes).Count -eq 2) 'Source plus creates additional note'
 $original=Rect $restored
 Check ($original.Left -eq $r.Left -and $original.Top -eq $r.Top) 'New source note does not move existing note'
 Send-Chord 0x4C
 $list=Wait-For { Root-Named 'OmniMemo · 메모 목록' } 'list'
 $rows=$list.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::ListItem)))
 Check ($rows.Count -eq 2) 'List contains both active notes'
 foreach($row in $rows) { (Named $row '메모 선택').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
 Invoke-Element (Named $list '선택 삭제')
 $confirm=Wait-For { $list.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) '선택 삭제')) } 'confirmation'
 $yes=Wait-For { foreach($w in (Roots)) { foreach($b in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Button)))) { if($b.Current.Name -match '^(예|Yes)'){return $b} } } } 'confirm yes'
 Invoke-Element $yes
 $listUndo=Wait-For { Named $list '삭제 실행 취소' } 'list undo'
 Start-Sleep -Milliseconds 600
 Check (@(Notes).Count -eq 0) 'List batch deletion hides both notes'
 Invoke-Element $listUndo
 [void](Wait-For { if(@(Notes).Count -eq 2){return $true} } 'list batch restored')
 Check (@(Notes).Count -eq 2) 'List undo restores whole selected batch'
} finally {
 if($process) { if(-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
 [void][HotkeySmokeNative]::SetCursorPos($cursor.X,$cursor.Y)
 $helper.Dispose(); $env:OMNIMEMO_DATA_DIR=$previous
 $log | Set-Content -LiteralPath (Join-Path $directory 'results.txt') -Encoding UTF8
 Write-Output ('UX 1.8 artifacts: '+$directory)
}