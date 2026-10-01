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
 do { if($process.HasExited) { throw ("App exited while waiting for "+$description+"; code "+$process.ExitCode) }; [System.Windows.Forms.Application]::DoEvents(); $result=& $probe; if($result) { return $result }; Start-Sleep -Milliseconds 150 } while([DateTime]::UtcNow -lt $end)
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
function Windows-Named([string]$name) {
 $conditions=[System.Windows.Automation.Condition[]]@(
  (Condition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty) $process.Id),
  (Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Window)),
  (Condition ([System.Windows.Automation.AutomationElement]::NameProperty) $name))
 $match=New-Object System.Windows.Automation.AndCondition (,$conditions)
 [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,$match) | Where-Object { -not $_.Current.IsOffscreen }
}
function Root-Named([string]$name) { Windows-Named $name | Select-Object -First 1 }
function Delete-Note($note) {
 Invoke-Element (Named $note '메모 설정')
 $action=Wait-For { foreach($w in (Roots)) { $item=Named $w '휴지통으로 이동'; if($item -and -not $item.Current.IsOffscreen){ return $item } } } 'delete menu'
 Invoke-Element $action
}
function Select-Tab($root,[string]$name) { (Named $root $name).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 200 }
function Visible-Named([string]$name) { foreach($w in (Roots)) { $all=$w.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) $name)); foreach($el in $all) {if(-not $el.Current.IsOffscreen){return $el}} } }
function Choose($root,[string]$combo,[string]$value) {
 $c=Named $root $combo
 $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 $item=Wait-For {Visible-Named $value} $value
 $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Milliseconds 150
}
function Note-Menu($note,[string]$group,[string]$item) {
 Invoke-Element (Named $note '메모 설정')
 $g=Wait-For { if($group -eq '글자 크기') {foreach($w in (Roots)){foreach($el in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)){if($el.Current.Name -like '글자 크기*' -and -not $el.Current.IsOffscreen){return $el}}}} else {Visible-Named $group} } $group
 $g.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 Invoke-Element (Wait-For {Visible-Named $item} $item)
 Start-Sleep -Milliseconds 100
}
function Dialog-Button([string]$prefix) {
 Wait-For {foreach($w in (Roots)){foreach($b in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Button)))){if($b.Current.Name -match $prefix -and -not $b.Current.IsOffscreen){return $b}}}} 'dialog button'
}
function File-Dialog([string]$title,[string]$path,[string]$buttonPattern){
 $dialog=Wait-For {Root-Named $title} $title
 $edit=Wait-For {
  $edits=$dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Edit)))
  foreach($e in $edits){if($e.Current.AutomationId -eq '1001' -or $e.Current.Name -match '파일 이름|File name'){return $e}}
 } 'filename edit'
 $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($path)
 $button=$dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Button))) | Where-Object {$_.Current.Name -match $buttonPattern} | Select-Object -First 1
 Invoke-Element $button
}
try {
 New-Item -ItemType Directory -Path $env:OMNIMEMO_DATA_DIR | Out-Null
 @{OverlayVisible=$true;Version=5;OverlayOpacity=1.0} | ConvertTo-Json | Set-Content (Join-Path $env:OMNIMEMO_DATA_DIR 'layout.json') -Encoding UTF8
 $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
 $note=Wait-For {foreach($w in (Notes)){return $w}} 'note'
 Set-Text $note '메모 내용' 'BACKUP_ORIGINAL_191'
 Start-Sleep -Milliseconds 800
 $panel=Wait-For {Root-Named 'OmniMemo · 데스크톱 패널'} 'panel'
 Invoke-Element (Named $panel '설정')
 $settings=Wait-For {Root-Named 'OmniMemo · 설정'} 'settings'
 Select-Tab $settings '백업'
 $backup=Join-Path $directory 'manual-test.db'
 Invoke-Element (Named $settings '백업 파일 저장…')
 File-Dialog '메모 전체 백업' $backup '^(저장|Save)'
 Invoke-Element (Dialog-Button '^(확인|OK)')
 Check ((Test-Path $backup) -and (Get-Item $backup).Length -gt 0) 'Manual backup file created through real dialog'
 $settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Set-Text $note '메모 내용' 'AFTER_BACKUP_191'
 Start-Sleep -Milliseconds 800
 Invoke-Element (Named $panel '설정')
 $settings=Wait-For {Root-Named 'OmniMemo · 설정'} 'settings reopened'
 Select-Tab $settings '백업'
 Invoke-Element (Named $settings '백업에서 복원…')
 File-Dialog '메모 백업 복원' $backup '^(열기|Open)'
 Invoke-Element (Dialog-Button '^(예|Yes)')
 Invoke-Element (Dialog-Button '^(확인|OK)')
 $note=Wait-For {foreach($w in (Notes)){if((Named $w '메모 내용').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'BACKUP_ORIGINAL_191'){return $w}}} 'restored original text'
 Check $true 'Manual backup restore replaces isolated edited content'
 if($settings -and -not $settings.Current.IsOffscreen){$settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}
 Delete-Note $note
 Send-Chord 0x46
 $list=Wait-For {Root-Named 'OmniMemo · 메모 목록'} 'list'
 Invoke-Element (Named $list '휴지통')
 Invoke-Element (Wait-For {Named $list '복원'} 'restore button')
 Invoke-Element (Named $list '전체 메모')
 $note=Wait-For {foreach($w in (Notes)){return $w}} 'trash restored note'
 Check $true 'Trash row Restore button restores note'
 Delete-Note $note
 Invoke-Element (Named $list '휴지통')
 Invoke-Element (Wait-For {Named $list '영구 삭제'} 'permanent delete')
 Invoke-Element (Dialog-Button '^(아니요|No)')
 Check ($null -ne (Named $list '영구 삭제')) 'Permanent delete cancel preserves isolated trash'
 Invoke-Element (Named $list '영구 삭제')
 Invoke-Element (Dialog-Button '^(예|Yes)')
 [void](Wait-For {if($null -eq (Named $list '영구 삭제')){return $true}} 'permanent deleted')
 Check $true 'Permanent delete confirm removes isolated trash'
} catch {$log.Add('FAIL: '+$_.Exception.Message);throw} finally {
 if($process){if(-not $process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 [void][HotkeySmokeNative]::SetCursorPos($cursor.X,$cursor.Y)
 $helper.Dispose();$env:OMNIMEMO_DATA_DIR=$previous
 $log | Set-Content -LiteralPath (Join-Path $directory 'results.txt') -Encoding UTF8
 Write-Output ('Backup audit artifacts: '+$directory)
}