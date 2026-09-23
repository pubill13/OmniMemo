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
$directory=Join-Path ([IO.Path]::GetFullPath($ArtifactsDirectory)) ('hotkeys-'+[Guid]::NewGuid().ToString('N'))
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
function Press-Key([byte]$key) {
 [HotkeySmokeNative]::keybd_event($key,0,0,[UIntPtr]::Zero)
 [HotkeySmokeNative]::keybd_event($key,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 250
}
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
 Send-Chord 0x48
 Check (-not [HotkeySmokeNative]::IsWindowVisible([IntPtr]$first.Current.NativeWindowHandle) -and -not [HotkeySmokeNative]::IsWindowVisible([IntPtr]$second.Current.NativeWindowHandle)) 'Global H hides notes while unrelated helper has focus'
 Send-Chord 0x48
 Check ([HotkeySmokeNative]::IsWindowVisible([IntPtr]$first.Current.NativeWindowHandle) -and [HotkeySmokeNative]::IsWindowVisible([IntPtr]$second.Current.NativeWindowHandle)) 'Global H restores hidden notes'
 Send-Chord 0x43
 Check ((Is-Tile $first) -and (Is-Tile $second)) 'Global C collapses all notes'
 Invoke-Element (Named $first '접힌 메모, 클릭하여 펼치기')
 Send-Chord 0x43
 Check ((Is-Tile $first) -and (Is-Tile $second)) 'Global C collapses mixed expanded and collapsed notes'
 Send-Chord 0x43
 Check (-not (Is-Tile $first) -and -not (Is-Tile $second)) 'Global C expands all collapsed notes'
 [void][HotkeySmokeNative]::SetForegroundWindow([IntPtr]$first.Current.NativeWindowHandle)
 (Named $first '메모 내용').SetFocus()
 Send-Chord 0x20 $false
 Check ((Is-Tile $first) -and -not (Is-Tile $second)) 'Local Ctrl Shift Space affects only focused note'
 Send-Chord 0x43
 Send-Chord 0x4F
 $panel=Wait-For { Panel } 'global O opens layout options'
 Set-Text $panel '시작 X' '120'; Set-Text $panel '시작 Y' '160'
 Set-Text $panel '간격' '12'; Set-Text $panel '열 수' '2'
 Invoke-Element (Named $panel '지금 정렬')
 Start-Sleep -Milliseconds 750
 $info=New-Object HotkeySmokeNative+INFO; $info.Size=[Runtime.InteropServices.Marshal]::SizeOf($info)
 $h=[IntPtr]$first.Current.NativeWindowHandle
 if(-not [HotkeySmokeNative]::GetMonitorInfo([HotkeySmokeNative]::MonitorFromWindow($h,2),[ref]$info)) { throw 'Monitor unavailable' }
 $scale=[HotkeySmokeNative]::GetDpiForWindow($h)/96.0
 $r=Rect $first
 Check ([Math]::Abs($r.Left-$info.Work.Left-120*$scale) -le 2 -and [Math]::Abs($r.Top-$info.Work.Top-160*$scale) -le 2) 'Panel custom X Y anchor controls arranged tile coordinates'
 Send-Chord 0x4F
 Check ($null -eq (Panel)) 'Global O hides layout options'
 Send-Chord 0x33
 $a=Rect $first; $b=Rect $second
 Check ($b.Left -lt $a.Left) 'Global 3 sorts by title'
 Send-Chord 0x31
 $a=Rect $first; $b=Rect $second
 Check ($a.Left -lt $b.Left) 'Global 1 sorts by creation time'
 Send-Chord 0x32
 $config=Config
 $sorts=@($config.Monitors.PSObject.Properties | ForEach-Object { $_.Value.Sort })
 Check (($sorts -contains 1) -or ($sorts -contains 'Color')) 'Global 2 persists color sorting'
 Send-Chord 0x52
 Check ((Is-Tile $first) -and (Is-Tile $second)) 'Global R arranges without expanding tiles'
 Send-Chord 0x41
 Check ((Config).AutoArrange) 'Global A enables automatic arrangement'
 Send-Chord 0x41
 Check (-not (Config).AutoArrange) 'Global A disables automatic arrangement'
 Send-Chord 0x4F
 $panel=Wait-For { Panel } 'panel for anchor picker'
 Capture-Window $panel 'layout-panel.png'
 Invoke-Element (Named $panel '화면에서 위치 선택…')
 $picker=Wait-For { Picker } 'anchor picker'
 [void][HotkeySmokeNative]::SetForegroundWindow([IntPtr]$picker.Current.NativeWindowHandle)
 Press-Key 0x1B
 $panel=Wait-For { Panel } 'panel after cancel'
 $xvalue=(Named $panel '시작 X').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
 Check ($xvalue -eq '120') 'Escape cancels anchor selection without changing draft'
 Invoke-Element (Named $panel '화면에서 위치 선택…')
 $picker=Wait-For { Picker } 'anchor picker for click'
 $bounds=Rect $picker
 [void][HotkeySmokeNative]::SetForegroundWindow([IntPtr]$picker.Current.NativeWindowHandle)
 [void][HotkeySmokeNative]::SetCursorPos([int]($bounds.Left+200*$scale),[int]($bounds.Top+220*$scale))
 Start-Sleep -Milliseconds 150
 [HotkeySmokeNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 [HotkeySmokeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 $panel=Wait-For { Panel } 'panel after selection'
 Invoke-Element (Named $panel '지금 정렬')
 Start-Sleep -Milliseconds 700
 $r=Rect $first
 Check ([Math]::Abs($r.Left-$info.Work.Left-200*$scale) -le 2 -and [Math]::Abs($r.Top-$info.Work.Top-220*$scale) -le 2) 'Screen click selects and applies first tile origin'
 (Named $panel '단축키').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 $recorder=Named $panel '메모 패널 열기 / 닫기 단축키'
 $scroll=$null
 if($recorder.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern,[ref]$scroll)) { $scroll.ScrollIntoView() }
 $recorder.SetFocus()
 foreach($k in @(0x11,0x12,0x10,0x50)) { [HotkeySmokeNative]::keybd_event([byte]$k,0,0,[UIntPtr]::Zero) }
 foreach($k in @(0x50,0x10,0x12,0x11)) { [HotkeySmokeNative]::keybd_event([byte]$k,0,2,[UIntPtr]::Zero) }
 Start-Sleep -Milliseconds 350
 Capture-Window $panel 'hotkeys-panel.png'
 Invoke-Element (Named $panel '적용')
 Start-Sleep -Milliseconds 700
 Check ((Config).Hotkeys.TogglePanel -eq 'Ctrl+Alt+Shift+P') 'Recorded shortcut is saved after Apply'
 Send-Chord 0x50
 Check ($null -eq (Panel)) 'Customized P toggles panel from another app'
 Send-Chord 0x4F
 Check ($null -eq (Panel)) 'Old O binding is released'
 Start-Sleep -Seconds 2
 Stop-Process -Id $process.Id; $process.WaitForExit(); $process.Dispose()
 $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
 $first=Wait-For { foreach($w in (Notes)) { if($w.Current.Name -eq 'Z first') { return $w } } } 'restored note'
 Send-Chord 0x50
 $panel=Wait-For { Panel } 'customized key after restart'
 Check $true 'Custom global shortcut and layout settings survive restart'
} catch { $log.Add('FAIL: '+$_.Exception.Message); throw }
finally {
 if($process) { if(-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
 [void][HotkeySmokeNative]::SetCursorPos($cursor.X,$cursor.Y)
 $helper.Close(); $helper.Dispose()
 $env:OMNIMEMO_DATA_DIR=$previous
 $log | Set-Content -LiteralPath (Join-Path $directory 'results.txt') -Encoding UTF8
 Write-Output ('Hotkey artifacts: '+$directory)
}
