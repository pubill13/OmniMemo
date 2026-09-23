param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [string]$ArtifactsDirectory = 'artifacts'
)
# Run with Windows PowerShell 5.1 -STA. Never uses personal OmniMemo data.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OmniSmokeNative {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll", EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr h, int index);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
}
'@
$exePath = (Resolve-Path -LiteralPath $Executable).Path
$runDirectory = Join-Path ([IO.Path]::GetFullPath($ArtifactsDirectory)) ('smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$priorDataDirectory = $env:OMNIMEMO_DATA_DIR
$env:OMNIMEMO_DATA_DIR = Join-Path $runDirectory 'data'
$ownedProcesses = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$results = New-Object 'System.Collections.Generic.List[string]'
$testText = "스모크 한글 저장 테스트`r`n받침: 읽었다. 😀"

function Assert-Smoke([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAIL: $Description" }
    $results.Add("PASS: $Description")
    Write-Output "PASS: $Description"
}
function Wait-For([scriptblock]$Probe, [string]$Description, [int]$Seconds=15) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $found = & $Probe
        if ($found) { return $found }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $Description"
}
function Start-Owned {
    $process = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) -WindowStyle Hidden -PassThru
    $ownedProcesses.Add($process)
    return $process
}
function Get-OwnWindows([int]$ProcessId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $ProcessId
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
    foreach ($window in $windows) { $window }
}
function Find-Name($Root, [string]$Name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $Name
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-Editor([int]$ProcessId) {
    foreach ($window in @(Get-OwnWindows $ProcessId)) {
        $editor = Find-Name $window '메모 내용'
        if ($editor -and -not $editor.Current.IsOffscreen) { return @{Window=$window; Editor=$editor} }
    }
}
function Get-Rect([IntPtr]$Handle) {
    $rect = New-Object OmniSmokeNative+RECT
    if (-not [OmniSmokeNative]::GetWindowRect($Handle, [ref]$rect)) { throw 'GetWindowRect failed.' }
    return $rect
}
function Test-Tile($Window) {
    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    $rect = Get-Rect $handle
    $expected = 36 * [OmniSmokeNative]::GetDpiForWindow($handle) / 96.0
    return ([Math]::Abs(($rect.Right-$rect.Left)-$expected) -le 2 -and [Math]::Abs(($rect.Bottom-$rect.Top)-$expected) -le 2)
}
function Save-WindowImage($Window, [string]$Name) {
    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    $rect = Get-Rect $handle
    $bitmap = New-Object System.Drawing.Bitmap ($rect.Right-$rect.Left), ($rect.Bottom-$rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { $captured = [OmniSmokeNative]::PrintWindow($handle, $dc, 2) }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    try {
        if ($captured) { $bitmap.Save((Join-Path $runDirectory $Name), [System.Drawing.Imaging.ImageFormat]::Png) }
        else { $results.Add('LIMITATION: PrintWindow capture unavailable; UI assertions still apply.') }
    } finally { $bitmap.Dispose() }
}
function Assert-NoAppWindow([int]$ProcessId) {
    foreach ($window in @(Get-OwnWindows $ProcessId)) {
        $handle = [IntPtr]$window.Current.NativeWindowHandle
        if ($handle -ne [IntPtr]::Zero -and [OmniSmokeNative]::IsWindowVisible($handle)) {
            Assert-Smoke (([OmniSmokeNative]::GetWindowLong($handle, -20) -band 0x40000) -eq 0) ('No WS_EX_APPWINDOW: ' + $window.Current.Name)
        }
    }
}

try {
    $process = Start-Owned
    $note = Wait-For { Find-Editor $process.Id } 'first-run note editor'
    $value = $note.Editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue($testText)
    Start-Sleep -Seconds 3
    Assert-Smoke ($value.Current.Value.Replace("`r`n", "`n") -eq $testText.Replace("`r`n", "`n")) 'Korean text entered'
    Save-WindowImage $note.Window 'expanded.png'
    $collapse = Find-Name $note.Window '작은 타일로 접기'
    Assert-Smoke ($null -ne $collapse) 'Collapse button accessible'
    $collapse.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $null = Wait-For { Test-Tile $note.Window } '36 DIP collapsed native window'
    Assert-Smoke (Test-Tile $note.Window) 'Collapsed physical dimensions equal 36 DIP at current DPI'
    Save-WindowImage $note.Window 'collapsed.png'
    Assert-NoAppWindow $process.Id
    Start-Sleep -Seconds 3
    # Simulate an ungraceful exit after autosave; terminate only the process created here.
    Stop-Process -Id $process.Id
    $process.WaitForExit()
    $process = Start-Owned
    $tile = Wait-For {
        foreach ($window in @(Get-OwnWindows $process.Id)) {
            if ($window.Current.Name -eq '스모크 한글 저장 테스트' -and (Test-Tile $window)) { return $window }
        }
    } 'persisted collapsed note after relaunch'
    Assert-Smoke (Test-Tile $tile) 'Collapsed state survives restart'
    $handle = [IntPtr]$tile.Current.NativeWindowHandle
    $tileButton = Find-Name $tile '접힌 메모, 클릭하여 펼치기'
    Assert-Smoke ($null -ne $tileButton) 'Collapsed tile exposes accessible expand action'
    $tileButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $note = Wait-For { Find-Editor $process.Id } 'tile expands on click'
    $persisted = $note.Editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Assert-Smoke ($persisted.Replace("`r`n", "`n") -eq $testText.Replace("`r`n", "`n")) 'Korean body survives restart'
    Save-WindowImage $note.Window 'reopened.png'
    $handle = [IntPtr]$note.Window.Current.NativeWindowHandle
    [void][OmniSmokeNative]::PostMessage($handle, 0x10, [IntPtr]0, [IntPtr]0)
    $null = Wait-For { -not [OmniSmokeNative]::IsWindowVisible($handle) } 'WM_CLOSE hides note'
    Assert-Smoke (-not $process.HasExited) 'Hiding note keeps process alive'
    $second = Start-Owned
    Assert-Smoke ($second.WaitForExit(10000)) 'Second instance exits'
    $main = Wait-For {
        foreach ($window in @(Get-OwnWindows $process.Id)) {
            if ($window.Current.Name -like '*OmniMemo*' -and -not $window.Current.IsOffscreen) { return $window }
        }
    } 'second instance opens existing list'
    Assert-Smoke ($null -ne $main) 'Existing process receives second-instance activation'
    Assert-NoAppWindow $process.Id
    $results.Add('LIMITATION: Tray mouse/context-menu flow not automated; no unrelated shell surfaces clicked.')
    $results.Add('LIMITATION: ValuePattern verifies Unicode persistence, not physical Korean IME composition.')
    $results.Add('LIMITATION: Only current display DPI tested; PrintWindow screenshots may be blank on some compositors.')
}
catch { $results.Add('FAIL: ' + $_.Exception.Message); throw }
finally {
    foreach ($owned in $ownedProcesses) {
        if (-not $owned.HasExited) { Stop-Process -Id $owned.Id -ErrorAction SilentlyContinue }
        $owned.Dispose()
    }
    $env:OMNIMEMO_DATA_DIR = $priorDataDirectory
    $results | Set-Content -LiteralPath (Join-Path $runDirectory 'results.txt') -Encoding UTF8
    Write-Output "Smoke artifacts: $runDirectory"
}


