param([Parameter(Mandatory=$true)][string]$Executable,[string]$ArtifactsDirectory='artifacts/verification',[switch]$ShowOverlay,[switch]$EdgeChecksOnly)
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
 [StructLayout(LayoutKind.Sequential)] public struct GUIINFO { public int Size,Flags; public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret; public RECT CaretRect; }
 [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint id,ref GUIINFO info);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,IntPtr unused);
 [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
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
function Test-EdgeInteractions($window) {
 $handle=[IntPtr]$window.Current.NativeWindowHandle
 $monitor=New-Object HotkeySmokeNative+INFO
 $monitor.Size=[Runtime.InteropServices.Marshal]::SizeOf($monitor)
 if(-not [HotkeySmokeNative]::GetMonitorInfo([HotkeySmokeNative]::MonitorFromWindow($handle,2),[ref]$monitor)) { throw 'Cannot inspect test monitor' }
 $area=$monitor.Work; $screen=$monitor.Monitor
 $scale=[HotkeySmokeNative]::GetDpiForWindow($handle)/96.0
 $middleX=[int](($area.Left+$area.Right)/2); $middleY=[int](($area.Top+$area.Bottom)/2)
 $thread=[HotkeySmokeNative]::GetWindowThreadProcessId($handle,[IntPtr]::Zero)
 function Center-Note {
  $r=Rect $window
  if(-not [HotkeySmokeNative]::SetWindowPos($handle,[IntPtr]::Zero,[int]($middleX-($r.Right-$r.Left)/2),[int]($middleY-($r.Bottom-$r.Top)/2),0,0,0x15)) { throw 'Cannot center isolated window' }
  Start-Sleep -Milliseconds 150
 }
 function Move-Pointer($fromX,$fromY,$toX,$toY,[scriptblock]$whileHeld) {
  [void][HotkeySmokeNative]::SetForegroundWindow($handle)
  Start-Sleep -Milliseconds 150
  if([HotkeySmokeNative]::GetForegroundWindow() -ne $handle) { throw 'Isolated note not foreground' }
  [void][HotkeySmokeNative]::SetCursorPos($fromX,$fromY)
  [HotkeySmokeNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  try {
   $pressed=New-Object HotkeySmokeNative+GUIINFO
   $pressed.Size=[Runtime.InteropServices.Marshal]::SizeOf($pressed)
   [void](Wait-For { if([HotkeySmokeNative]::GetGUIThreadInfo($thread,[ref]$pressed) -and ($pressed.Capture -eq $handle -or $pressed.MoveSize -eq $handle)) { return $true } } 'mouse press handled by isolated note')
   for($i=1;$i -le 16;$i++) {
    [void][HotkeySmokeNative]::SetCursorPos([int]($fromX+($toX-$fromX)*$i/16),[int]($fromY+($toY-$fromY)*$i/16))
    Start-Sleep -Milliseconds 20
   }
   Start-Sleep -Milliseconds 350
   if($whileHeld) { & $whileHeld }
  } finally { [HotkeySmokeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
  Start-Sleep -Milliseconds 200
 }
 foreach($edge in @('top','top-right','left','right','magnet')) {
  Center-Note
  $before=Rect $window
  $gripX=[int](140*$scale); $gripY=[int](12*$scale)
  $toX=$middleX; $toY=$middleY
  switch($edge) {
   'top' { $toY=$screen.Top }
   'top-right' { $toX=$screen.Right-1; $toY=$screen.Top }
   'left' { $toX=$screen.Left }
   'right' { $toX=$screen.Right-1 }
   'magnet' { $toX=$area.Left+$gripX+3; $toY=$area.Top+$gripY+3 }
  }
  Move-Pointer ($before.Left+$gripX) ($before.Top+$gripY) $toX $toY {
   $info=New-Object HotkeySmokeNative+GUIINFO
   $info.Size=[Runtime.InteropServices.Marshal]::SizeOf($info)
   if(-not [HotkeySmokeNative]::GetGUIThreadInfo($thread,[ref]$info)) { throw 'GetGUIThreadInfo failed' }
   Check (($info.Flags -band 2) -eq 0 -and $info.Capture -eq $handle) "Header owns pointer without Windows move/size loop at $edge"
  }
  $after=Rect $window
  Check (($after.Right-$after.Left) -eq ($before.Right-$before.Left) -and ($after.Bottom-$after.Top) -eq ($before.Bottom-$before.Top) -and -not [HotkeySmokeNative]::IsZoomed($handle)) "Released note keeps size at $edge"
  Check ([Math]::Abs($after.Left-($toX-$gripX)) -le (12*$scale) -and [Math]::Abs($after.Top-($toY-$gripY)) -le (12*$scale)) "Pointer moves note to $edge (actual $($after.Left),$($after.Top); expected $($toX-$gripX),$($toY-$gripY))"
  if($edge -eq 'magnet') { Check ($after.Left -eq $area.Left -and $after.Top -eq $area.Top) 'Screen edge magnet snaps within threshold' }
 }
 Center-Note
 $before=Rect $window
 Move-Pointer ($before.Right-2) $middleY ($before.Right+38) $middleY $null
 $after=Rect $window
 Check ([Math]::Abs($after.Right-$before.Right-40) -le 2 -and $after.Left -eq $before.Left) 'Manual border resize retained'
}
Add-Type -ReferencedAssemblies @([System.Windows.Automation.AutomationElement].Assembly.Location,[System.Windows.Automation.AutomationProperty].Assembly.Location) @"
using System;
using System.Threading;
using System.Windows.Automation;
public static class EnabledRecorder {
 public static int Disabled;
 private static AutomationPropertyChangedEventHandler handler = (s,e) => { if(e.Property==AutomationElement.IsEnabledProperty && Equals(e.NewValue,false)) Interlocked.Increment(ref Disabled); };
 public static void Start(AutomationElement element) { Disabled=0; Automation.AddAutomationPropertyChangedEventHandler(element,TreeScope.Subtree,handler,AutomationElement.IsEnabledProperty); }
 public static void Stop(AutomationElement element) { Automation.RemoveAutomationPropertyChangedEventHandler(element,handler); }
}
"@
try {
 New-Item -ItemType Directory -Path $env:OMNIMEMO_DATA_DIR | Out-Null
 @{OverlayVisible=[bool]$ShowOverlay; Version=5} | ConvertTo-Json | Set-Content (Join-Path $env:OMNIMEMO_DATA_DIR 'layout.json') -Encoding UTF8
 $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
 $first=Wait-For { foreach($w in (Notes)) { return $w } } 'first note'
 if($EdgeChecksOnly) { Test-EdgeInteractions $first; return }
 Drag-Note $first 140 12
 $editor=Named $first '메모 내용'; $editor.SetFocus()
 [EnabledRecorder]::Start($first)
 foreach($v in @('a','ab','abc','abcd')) { Set-Text $first '메모 내용' $v; Start-Sleep -Milliseconds 130; Check $editor.Current.HasKeyboardFocus ('Editor retains focus after '+$v.Length+' characters') }
 Start-Sleep -Milliseconds 750
 Check $editor.Current.HasKeyboardFocus 'Editor retains focus after autosave'
 Set-Text $first '메모 내용' ''
 foreach($character in 'abcdefghijkl'.ToCharArray()) {
  Check ($editor.Current.HasKeyboardFocus -and [HotkeySmokeNative]::GetForegroundWindow() -eq [IntPtr]$first.Current.NativeWindowHandle) 'Target focused before keyboard input'
  [System.Windows.Forms.SendKeys]::SendWait([string]$character)
  Start-Sleep -Milliseconds 220
 }
 Start-Sleep -Milliseconds 750
 $value=$editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
 Check ($value -eq 'abcdefghijkl') 'Continuous keyboard typing survives max-delay and idle autosave without lost or reordered characters'
 Check $editor.Current.HasKeyboardFocus 'Keyboard focus stays in editor after continuous typing'
 Check ([EnabledRecorder]::Disabled -eq 0) 'Typing never disables the note'
 [EnabledRecorder]::Stop($first)
 Drag-Note $first 140 12
 $title=Named $first 'OmniMemo'
 $scale=[HotkeySmokeNative]::GetDpiForWindow([IntPtr]$first.Current.NativeWindowHandle)/96.0
 $dotX=[int](($title.Current.BoundingRectangle.Right-(Rect $first).Left)/$scale+8)
 Drag-Note $first $dotX 12
} finally {
 if($process) { if(-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
 [void][HotkeySmokeNative]::SetCursorPos($cursor.X,$cursor.Y)
 $helper.Dispose(); $env:OMNIMEMO_DATA_DIR=$previous
 $log | Set-Content -LiteralPath (Join-Path $directory 'results.txt') -Encoding UTF8
 Write-Output ('Input drag artifacts: '+$directory)
}
