<#
.SYNOPSIS
Checks a published Pocketknife.exe end to end, silently: runs every tool on the test fixtures, checks the launcher
and the global hotkey, and checks the input files were not changed.

.DESCRIPTION
Run on an isolated Windows desktop that is never displayed. UI Automation can activate even an off-screen
window; the script refuses to run on the input desktop. The app runs with POCKETKNIFE_HIDDEN=1, so its windows open off-screen
without taking focus, a taskbar button or a tray icon. The script never moves focus and never sends keystrokes or
mouse input: it fills fields and presses buttons through UI Automation patterns, opens tools with the app's
--tool command line, and tests the hotkey with Ctrl+Alt+Shift+F24 (there is no F24 key on normal keyboards) by
posting WM_HOTKEY to the app's own message window. Screenshots come from the headless app tests instead
(set POCKETKNIFE_SCREENSHOTS when running tests/Pocketknife.App.Tests).

.PARAMETER Exe
Path to Pocketknife.exe.
.PARAMETER Work
Scratch folder, new or made by an earlier run of this script. Sample inputs go in Samples\, results in
Downloads\Pocketknife\ and the app's data in data\, so your own settings and Downloads are untouched.
.PARAMETER EngineCache
Optional engines folder to copy in first (for example the test cache), so only missing engines download.
#>
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Work,
    [string]$EngineCache,
    [ValidateSet("System", "Light", "Dark")][string]$Theme = "Light",
    [string]$VideoUrl = "https://www.youtube.com/watch?v=jNQXAC9IVRw"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SmokeNative {
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, System.Text.StringBuilder value, uint length, out uint needed);
    private static string DesktopName(IntPtr desktop) {
        var name = new System.Text.StringBuilder(256);
        uint needed;
        if (desktop == IntPtr.Zero || !GetUserObjectInformation(desktop, 2, name, 512, out needed))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return name.ToString();
    }
    public static bool UsesInputDesktop() {
        var input = OpenInputDesktop(0, false, 1);
        try { return DesktopName(GetThreadDesktop(GetCurrentThreadId())) == DesktopName(input); }
        finally { if (input != IntPtr.Zero) CloseDesktop(input); }
    }
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
"@

if ([SmokeNative]::UsesInputDesktop()) {
    throw "Run this smoke test on an isolated Windows desktop that is never displayed. UI Automation may otherwise take focus."
}

$A = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
function Cond($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

$hotkey = "Ctrl+Alt+Shift+F24"
$Work = [IO.Path]::GetFullPath($Work)
$data = Join-Path $Work "data"
$outputs = Join-Path $Work "Downloads\Pocketknife"
$inputs = Join-Path $Work "Samples"
$marker = Join-Path $Work ".pocketknife-smoke"
if ((Test-Path $Work) -and -not (Test-Path $marker)) { throw "$Work exists and was not created by this script. Choose a new folder." }
Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $data, $outputs, $inputs | Out-Null
New-Item -ItemType File $marker | Out-Null
$fixtures = Join-Path $PSScriptRoot "..\tests\Pocketknife.Tests\Fixtures"
$samples = @{ "sample.mp4" = "holiday-clip.mp4"; "sample.pdf" = "invoice.pdf"; "speech.mp3" = "voice-note.mp3"; "subject.png" = "product-photo.png" }
foreach ($name in $samples.Keys) { Copy-Item (Join-Path $fixtures $name) (Join-Path $inputs $samples[$name]) }
Copy-Item (Join-Path $inputs "invoice.pdf") (Join-Path $inputs "receipt.pdf")
if ($EngineCache) { Copy-Item $EngineCache (Join-Path $data "engines") -Recurse }
@{ OutputRoot = $outputs; Theme = $Theme; KeepRunningInTray = $true; LauncherHotkey = $hotkey } |
    ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $data "settings.json")
$before = Get-ChildItem $inputs | ForEach-Object { "$($_.Name)=$((Get-FileHash $_.FullName).Hash)" }

$env:POCKETKNIFE_DATA = $data
$env:POCKETKNIFE_HIDDEN = "1"
$process = Start-Process $Exe -PassThru -WindowStyle Hidden

function Wait-For([scriptblock]$probe, [int]$seconds = 30, [string]$what = "condition") {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $value = & $probe
        if ($value) { return $value }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for $what"
}

function Get-Window([string]$name) {
    Wait-For {
        $A::RootElement.FindFirst($Tree::Children, (New-Object System.Windows.Automation.AndCondition(
            (Cond $A::ProcessIdProperty $process.Id), (Cond $A::NameProperty $name))))
    } 30 "window '$name'"
}

function Find([System.Windows.Automation.AutomationElement]$root, $property, $value, [int]$seconds = 10) {
    Wait-For { $root.FindFirst($Tree::Descendants, (Cond $property $value)) } $seconds "element $value"
}

function Find-Control($window, $property, [string]$value, $type) {
    Wait-For {
        $window.FindFirst($Tree::Descendants, (New-Object System.Windows.Automation.AndCondition(
            (Cond $property $value), (Cond $A::ControlTypeProperty $type))))
    } 10 "$type $value"
}

function Set-Text($window, $property, [string]$value, [string]$text) {
    $box = Find-Control $window $property $value ([System.Windows.Automation.ControlType]::Edit)
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

function Set-Toggle($window, [string]$name) {
    $box = Find-Control $window $A::NameProperty $name ([System.Windows.Automation.ControlType]::CheckBox)
    $box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}

function Launcher-Rows($launcher) {
    Start-Sleep -Milliseconds 300
    $list = Find $launcher $A::AutomationIdProperty "LauncherResults"
    @($list.FindAll($Tree::Children, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::ListItem))) | ForEach-Object { $_.Current.Name })
}

# Opens a tool by handing --tool to the running copy (the second process has no window and exits at once).
function Open-Tool([string]$id, [string[]]$paths) {
    Start-Process $Exe -ArgumentList (@("--tool", $id) + ($paths | ForEach-Object { "`"$_`"" })) -Wait -WindowStyle Hidden
    Start-Sleep -Milliseconds 800
    $window = Get-Window "Pocketknife"
    Wait-For { $window.FindFirst($Tree::Descendants, (Cond $A::AutomationIdProperty "RunButton")) } 10 "tool page $id" | Out-Null
    return $window
}

function Run-Tool($window, [int]$seconds = 300) {
    (Find $window $A::AutomationIdProperty "RunButton").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $status = Find $window $A::AutomationIdProperty "StatusText"
    Wait-For {
        $text = $status.Current.Name
        if ($text -and -not $text.EndsWith([string][char]0x2026)) { $text }
    } $seconds "run to finish" | Out-Null
    $summary = $window.FindFirst($Tree::Descendants, (Cond $A::AutomationIdProperty "ResultSummary"))
    return @{ Status = $status.Current.Name; Summary = if ($summary) { $summary.Current.Name } else { "" } }
}

function Check-Run([string]$id, [hashtable]$result) {
    Write-Host ("{0,-20} {1} | {2}" -f $id, $result.Status, $result.Summary)
    if ($result.Status -ne "Done") { throw "$id did not finish cleanly: $($result.Status)" }
}

function Expect-Rows($launcher, [string]$query, [scriptblock]$check, [string]$what) {
    Set-Text $launcher $A::AutomationIdProperty "LauncherQuery" $query
    $rows = Launcher-Rows $launcher
    if (-not (& $check $rows)) { throw "Launcher '$query': expected $what, got '$($rows -join " | ")'" }
    Write-Host ("launcher {0,-16} {1}" -f "'$query'", ($rows -join " | "))
}

try {
    Get-Window "Pocketknife" | Out-Null

    # The app holds the hotkey: Windows refuses to give the same shortcut to anyone else (ERROR_HOTKEY_ALREADY_REGISTERED).
    if ([SmokeNative]::RegisterHotKey([IntPtr]::Zero, 0x7151, 0x2 -bor 0x1 -bor 0x4 -bor 0x4000, 0x87)) {
        [SmokeNative]::UnregisterHotKey([IntPtr]::Zero, 0x7151) | Out-Null
        throw "$hotkey was free, so the app didn't register it."
    }
    if ([Runtime.InteropServices.Marshal]::GetLastWin32Error() -ne 1409) { throw "Unexpected RegisterHotKey error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())." }

    # Simulate the key press by delivering WM_HOTKEY to the app's own message window: no real keystrokes.
    $hotkeyWindow = [IntPtr]::Zero
    do {
        $hotkeyWindow = [SmokeNative]::FindWindowEx([IntPtr]-3, $hotkeyWindow, [NullString]::Value, "PocketknifeHotkey")
        $owner = 0
        if ($hotkeyWindow -ne [IntPtr]::Zero) { [SmokeNative]::GetWindowThreadProcessId($hotkeyWindow, [ref]$owner) | Out-Null }
    } until ($hotkeyWindow -eq [IntPtr]::Zero -or $owner -eq $process.Id)
    if ($hotkeyWindow -eq [IntPtr]::Zero) { throw "The app's hotkey window was not found." }
    [SmokeNative]::PostMessage($hotkeyWindow, 0x0312, [IntPtr]0x5442, [IntPtr]::Zero) | Out-Null
    $launcher = Get-Window "Find a tool"
    Write-Host "launcher hotkey      $hotkey registered; WM_HOTKEY opened the launcher"

    Expect-Rows $launcher "gif" { param($r) $r[0] -eq "Video to GIF" -and $r[-1] -eq "Search DuckDuckGo for 'gif'" } "Video to GIF first and the web search last"
    Expect-Rows $launcher "12*(3+4)" { param($r) $r[0] -eq "= 84" } "the calculation first"
    Expect-Rows $launcher "!yt lofi beats" { param($r) ($r -join "|") -eq "Search YouTube for 'lofi beats'" } "only the YouTube search"
    Expect-Rows $launcher "zzqx weather" { param($r) ($r -join "|") -eq "Search DuckDuckGo for 'zzqx weather'" } "only the default web search"

    $w = Open-Tool "compress-video" @("$inputs\holiday-clip.mp4")
    Check-Run "compress-video" (Run-Tool $w)

    $w = Open-Tool "convert-media" @("$inputs\holiday-clip.mp4")
    Check-Run "convert-media" (Run-Tool $w)

    $w = Open-Tool "video-to-gif" @("$inputs\holiday-clip.mp4")
    Check-Run "video-to-gif" (Run-Tool $w)

    $w = Open-Tool "download-video" @()
    Set-Text $w $A::AutomationIdProperty "TextInput" $VideoUrl
    Check-Run "download-video" (Run-Tool $w)

    $w = Open-Tool "remove-background" @("$inputs\product-photo.png")
    Check-Run "remove-background" (Run-Tool $w)

    $w = Open-Tool "merge-pdfs" @("$inputs\invoice.pdf", "$inputs\receipt.pdf")
    Check-Run "merge-pdfs" (Run-Tool $w)

    $w = Open-Tool "split-pdf" @("$inputs\invoice.pdf")
    Check-Run "split-pdf" (Run-Tool $w)

    $w = Open-Tool "redact-pdf" @("$inputs\invoice.pdf")
    Set-Text $w $A::NameProperty "Words or phrases to redact" "Jane Doe`r`n4111-2222-3333"
    Set-Toggle $w "Redact email addresses"
    Set-Toggle $w "Redact phone numbers"
    Check-Run "redact-pdf" (Run-Tool $w)

    $w = Open-Tool "transcribe" @("$inputs\voice-note.mp3")
    Check-Run "transcribe" (Run-Tool $w 900)

    $w = Open-Tool "make-qr-code" @()
    Set-Text $w $A::AutomationIdProperty "TextInput" "https://github.com/kenhendricks00/Pocketknife"
    Check-Run "make-qr-code" (Run-Tool $w)

    $qr = Get-ChildItem $outputs -Recurse -Filter "QR code.png" | Select-Object -First 1
    $w = Open-Tool "read-qr-code" @($qr.FullName)
    Check-Run "read-qr-code" (Run-Tool $w)
    $decoded = (Find $w $A::AutomationIdProperty "Messages").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($decoded -ne "https://github.com/kenhendricks00/Pocketknife") { throw "QR read back '$decoded'" }
}
finally {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    # Wait until it is gone, so a following run doesn't race this one for the single-instance pipe and the hotkey.
    Wait-Process -Id $process.Id -Timeout 15 -ErrorAction SilentlyContinue
    Remove-Item Env:\POCKETKNIFE_DATA, Env:\POCKETKNIFE_HIDDEN -ErrorAction SilentlyContinue
}

# Leave nothing running: every copy of this exe (the app and any --tool hand-off) must be gone.
$exePath = (Resolve-Path $Exe).Path
$left = Wait-For { $running = @(Get-CimInstance Win32_Process -Filter "Name='$(Split-Path $exePath -Leaf)'" | Where-Object ExecutablePath -eq $exePath); if ($running.Count -eq 0) { "none" } } 20 "the app to exit"
Write-Host "app processes left   $left"

$after = Get-ChildItem $inputs | ForEach-Object { "$($_.Name)=$((Get-FileHash $_.FullName).Hash)" }
if (Compare-Object $before $after) { throw "Input files changed!" }
Write-Host "inputs unchanged     $($before.Count) files, same SHA-256"
Write-Host ""
Get-ChildItem $outputs -Recurse -File | ForEach-Object { "{0,10:N0}  {1}" -f $_.Length, $_.FullName.Substring($outputs.Length + 1) }
