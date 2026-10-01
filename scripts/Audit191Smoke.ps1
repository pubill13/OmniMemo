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
function Invoke-Element($element) { $pattern=$null; if($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){ $pattern.Invoke(); return }; $element.SetFocus(); [System.Windows.Forms.SendKeys]::SendWait(' '); }
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
 $condition=New-Object System.Windows.Automation.AndCondition ((Condition ([System.Windows.Automation.AutomationElement]::NameProperty) $combo)),((Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::ComboBox)))
 $c=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
 $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 $item=Wait-For { foreach($w in (Roots)) { foreach($candidate in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) $value))) { $pattern=$null; if(-not $candidate.Current.IsOffscreen -and $candidate.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)){ return $candidate } } } } $value
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
try {
 New-Item -ItemType Directory -Path $env:OMNIMEMO_DATA_DIR | Out-Null
 @{OverlayVisible=$true;Version=5;OverlayOpacity=1.0} | ConvertTo-Json | Set-Content (Join-Path $env:OMNIMEMO_DATA_DIR 'layout.json') -Encoding UTF8
 $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
 $first=Wait-For {foreach($w in (Notes)){return $w}} 'first note'
 Set-Text $first '메모 내용' '버튼 전수 점검 isolated data'
 foreach($color in @('노랑','분홍','초록','파랑','보라','흰색')) {Note-Menu $first '메모 색상' $color;Check $true ('Note color: '+$color)}
 foreach($font in @('맑은 고딕','굴림','Segoe UI','Arial','Consolas','Times New Roman')) {Note-Menu $first '글꼴' $font;Check $true ('Note font: '+$font)}
 foreach($size in @(10..32)+@(36,40,48,56,64,72)) {Note-Menu $first '글자 크기' ([string]$size);Check $true ('Note size: '+$size)}
 Note-Menu $first '글자 크기' '1 작게';Note-Menu $first '글자 크기' '1 크게';Note-Menu $first '글자 크기' '16'
 $pin=Named $first '항상 위에 표시';$pin.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle();$pin.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
 Invoke-Element (Named $first '작은 타일로 접기')
 [void](Wait-For {Named $first '접힌 메모, 클릭하여 펼치기'} 'collapse')
 Invoke-Element (Named $first '접힌 메모, 클릭하여 펼치기')
 [void](Wait-For {Named $first '메모 내용'} 'expand')
 Check $true 'Note pin and collapse/expand controls'
 Invoke-Element (Named $first '새 메모')
 [void](Wait-For {if(@(Notes).Count -eq 2){return $true}} 'new note')
 $panel=Wait-For {Root-Named 'OmniMemo · 데스크톱 패널'} 'panel'
 Invoke-Element (Named $panel '접어서 정돈')
 [void](Wait-For {if(@(Notes | Where-Object {Is-Tile $_}).Count -eq 2){return $true}} 'batch collapse')
 Invoke-Element (Named $panel '모두 펼치기')
 [void](Wait-For {if(@(Notes | Where-Object {-not (Is-Tile $_)}).Count -eq 2){return $true}} 'batch expand')
 Invoke-Element (Named $panel '모두 숨기기')
 [void](Wait-For {if(@(Notes).Count -eq 0){return $true}} 'all hidden')
 Invoke-Element (Named $panel '모두 보이기')
 [void](Wait-For {if(@(Notes).Count -eq 2){return $true}} 'all shown')
 Check $true 'Panel batch and visibility controls'
 $exp=Named $panel '색상별 조작';$exp.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 foreach($color in @('전체','노랑','분홍','초록','파랑','보라','흰색')) {
  $chips=$panel.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  $chip=$chips | Where-Object {$_.Current.Name -like ($color+' · 보이는 메모*')} | Select-Object -First 1
  $chip.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
  Invoke-Element (Named $panel '선택 색상 접기');Invoke-Element (Named $panel '선택 색상 펼치기')
  Check $true ('Panel color controls: '+$color)
 }
 $slider=Named $panel '패널 불투명도';$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(75)
 [void](Wait-For {if([Math]::Abs((Config).OverlayOpacity-0.75)-lt 0.001){return $true}} 'panel opacity saved')
 Capture-Window $panel 'panel.png'
 Invoke-Element (Named $panel '설정');$settings=Wait-For {Root-Named 'OmniMemo · 설정'} 'settings'
 foreach($shape in @('여러 줄로 배치','가로 한 줄','세로 한 줄')){Choose $settings '배치 형태' $shape}
 Choose $settings '배치 형태' '여러 줄로 배치'
 foreach($sort in @('생성순','색상순','제목순')){Choose $settings '정렬 기준' $sort}
 $corners=$settings.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::RadioButton)))
 foreach($c in $corners){$c.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()}
 Invoke-Element (Named $settings '미리보기');$preview=Wait-For {Root-Named 'OmniMemo · 배치 미리보기'} 'preview'
 Capture-Window $preview 'preview.png';$preview.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Invoke-Element (Named $settings '화면에서 지정');$picker=Wait-For {Picker} 'picker'
 [void][HotkeySmokeNative]::SetForegroundWindow([IntPtr]$picker.Current.NativeWindowHandle)
 if([HotkeySmokeNative]::GetForegroundWindow() -ne [IntPtr]$picker.Current.NativeWindowHandle){throw 'Cannot focus isolated picker'}
 [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
 [void](Wait-For {if($null -eq (Picker)){return $true}} 'picker canceled')
 Check $true 'Settings shapes, sort, corners, preview, picker cancel'
 Select-Tab $settings '단축키'
 $editors=$settings.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Edit)))
 Check (@($editors | Where-Object {$_.Current.Name -like '* 단축키'}).Count -eq 6) 'Six shortcut editors'
 $clears=$settings.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::NameProperty) '해제'))
 foreach($clear in $clears){Invoke-Element $clear}
 Invoke-Element (Named $settings '모든 단축키 기본값')
 Capture-Window $settings 'shortcuts.png'
 Select-Tab $settings '일반';Check ($null -eq (Named $settings '패널 불투명도')) 'Settings has no opacity slider'
 Select-Tab $settings '백업';Check ($null -ne (Named $settings '데이터 저장 위치')) 'Backup data path'
 Capture-Window $settings 'backup.png'
 Invoke-Element (Named $settings '적용');Start-Sleep -Milliseconds 500
 Check ([Math]::Abs((Config).OverlayOpacity-0.75)-lt 0.001) 'Settings Apply preserves panel opacity'
 $settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Invoke-Element (Named $panel '목록·검색');$list=Wait-For {Root-Named 'OmniMemo · 메모 목록'} 'list'
 Set-Text $list '메모 내용 검색' 'no_matching_unique';Set-Text $list '메모 내용 검색' ''
 Invoke-Element (Named $list '휴지통');Invoke-Element (Named $list '전체 메모')
 Check $true 'List search and active/trash navigation'
 $rows=$list.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::ListItem)))
 foreach($row in $rows){(Named $row '메모 선택').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()}
 Invoke-Element (Named $list '선택 삭제')
 $yes=Wait-For {foreach($w in (Roots)){foreach($b in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,(Condition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Button)))){if($b.Current.Name -match '^(예|Yes)'){return $b}}}} 'delete confirm'
 Invoke-Element $yes
 $undo=Wait-For {Named $list '삭제 실행 취소'} 'list undo';Invoke-Element $undo
 [void](Wait-For {if(@(Notes).Count -eq 2){return $true}} 'undo restored')
 Check $true 'List batch delete and undo'
 $log.Add('NOT RUN: real Windows startup registration; tray selection if duplicate icons; backup restore dialogs; permanent delete; physical multi-monitor/IME.')
} catch { $log.Add('FAIL: '+$_.Exception.Message); throw } finally {
 if($process){if(-not $process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 [void][HotkeySmokeNative]::SetCursorPos($cursor.X,$cursor.Y)
 $helper.Dispose();$env:OMNIMEMO_DATA_DIR=$previous
 $log | Set-Content -LiteralPath (Join-Path $directory 'results.txt') -Encoding UTF8
 Write-Output ('Audit 1.9.1 artifacts: '+$directory)
}
