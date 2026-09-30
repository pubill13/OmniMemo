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
  for($i=1;$i -le 10;$i++) { [void][HotkeySmokeNative]::SetCursorPos($sx-10*$i,$sy+6*$i); Start-Sleep -Milliseconds 40 }
  $during=Rect $w
 } finally { [HotkeySmokeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
 Start-Sleep -Milliseconds 300
 $after=Rect $w
 Write-Output "Drag ($localX,$localY): before=$($r.Left),$($r.Top) during=$($during.Left),$($during.Top) after=$($after.Left),$($after.Top)"
 Check ([Math]::Abs($during.Left-$r.Left+100) -le 15 -and [Math]::Abs($during.Top-$r.Top-60) -le 15) 'Dragging moves note with cursor'
 Check ($after.Left -eq $during.Left -and $after.Top -eq $during.Top) 'Released expanded note stays at manually dragged position'
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
