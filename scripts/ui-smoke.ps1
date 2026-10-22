<#
.SYNOPSIS
Drives the real app through UI Automation: opens every tool, runs it on the test fixtures, checks the result and
saves a screenshot of each screen. Also presses the global launcher hotkey and checks the launcher appears.

.PARAMETER Exe
Path to WindowsToolbox.exe.
.PARAMETER Work
Scratch folder. The app runs with its data and output folders inside it, so your own settings are untouched.
.PARAMETER Screenshots
Where to save PNG screenshots.
.PARAMETER EngineCache
Optional engines folder to copy in first (for example the test cache), so only missing engines download.
#>
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Work,
    [Parameter(Mandatory)][string]$Screenshots,
    [string]$EngineCache,
    [ValidateSet("System", "Light", "Dark")][string]$Theme = "Light",
    [string]$VideoUrl = "https://www.youtube.com/watch?v=jNQXAC9IVRw"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class Native {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static Bitmap Capture(IntPtr hwnd) {
        SetForegroundWindow(hwnd);
        System.Threading.Thread.Sleep(400);
        RECT r; DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT)));
        var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using (var g = Graphics.FromImage(bmp)) { g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size); }
        return bmp;
    }
    public static void Chord(byte[] keys) {
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        Array.Reverse(keys);
        foreach (var k in keys) keybd_event(k, 0, 2, UIntPtr.Zero);
    }
}
"@ -ReferencedAssemblies System.Drawing
[Native]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null

$A = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
function Cond($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

$Work = [IO.Path]::GetFullPath($Work)
$data = Join-Path $Work "data"
$outputs = Join-Path $Work "outputs"
$inputs = Join-Path $Work "inputs"
Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $data, $outputs, $inputs, $Screenshots | Out-Null
Copy-Item (Join-Path $PSScriptRoot "..\tests\Toolbox.Tests\Fixtures\*") $inputs
Copy-Item (Join-Path $inputs "sample.pdf") (Join-Path $inputs "second.pdf")
if ($EngineCache) { Copy-Item $EngineCache (Join-Path $data "engines") -Recurse }
@{ OutputRoot = $outputs; Theme = $Theme; KeepRunningInTray = $true; LauncherHotkey = "Ctrl+Alt+Space" } |
    ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $data "settings.json")
$before = Get-ChildItem $inputs | ForEach-Object { "$($_.Name)=$((Get-FileHash $_.FullName).Hash)" }

$env:WINDOWS_TOOLBOX_DATA = $data
$process = Start-Process $Exe -PassThru

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

function Save-Shot($element, [string]$name) {
    Start-Sleep -Milliseconds 600
    $bitmap = [Native]::Capture([IntPtr]$element.Current.NativeWindowHandle)
    $bitmap.Save((Join-Path $Screenshots "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "  screenshot $name.png"
}

function Open-Tool([string]$id, [string[]]$paths) {
    Start-Process $Exe -ArgumentList (@("--tool", $id) + ($paths | ForEach-Object { "`"$_`"" })) -Wait
    Start-Sleep -Milliseconds 800
    $window = Get-Window "Windows Toolbox"
    Wait-For { $window.FindFirst($Tree::Descendants, (Cond $A::AutomationIdProperty "RunButton")) } 10 "tool page $id" | Out-Null
    return $window
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

$results = [ordered]@{}
function Check-Run([string]$id, $window, [hashtable]$result, [string]$shot) {
    Save-Shot $window $shot
    $ok = $result.Status -eq "Done"
    $results[$id] = "$($result.Status) | $($result.Summary)"
    Write-Host ("{0,-20} {1}" -f $id, $results[$id])
    if (-not $ok) { throw "$id did not finish cleanly: $($result.Status)" }
}

try {
    $main = Get-Window "Windows Toolbox"
    Save-Shot $main "01-home"

    [Native]::Chord(@(0x11, 0x12, 0x20))
    $launcher = Get-Window "Find a tool"
    Set-Text $launcher $A::AutomationIdProperty "LauncherQuery" "gif"
    Save-Shot $launcher "02-launcher"
    $first = (Find $launcher $A::AutomationIdProperty "LauncherResults").FindFirst($Tree::Children, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::ListItem)))
    if ($first.Current.Name -ne "Video to GIF") { throw "Launcher's first result was '$($first.Current.Name)'" }
    Write-Host "launcher hotkey      opened, 'gif' -> Video to GIF"
    [Native]::Chord(@(0x1B))

    $w = Open-Tool "compress-video" @("$inputs\sample.mp4")
    Check-Run "compress-video" $w (Run-Tool $w) "10-compress-video"

    $w = Open-Tool "convert-media" @("$inputs\sample.mp4")
    Check-Run "convert-media" $w (Run-Tool $w) "11-convert-media"

    $w = Open-Tool "video-to-gif" @("$inputs\sample.mp4")
    Check-Run "video-to-gif" $w (Run-Tool $w) "12-video-to-gif"

    $w = Open-Tool "download-video" @()
    Set-Text $w $A::AutomationIdProperty "TextInput" $VideoUrl
    Check-Run "download-video" $w (Run-Tool $w) "13-download-video"

    $w = Open-Tool "remove-background" @("$inputs\subject.png")
    Check-Run "remove-background" $w (Run-Tool $w) "14-remove-background"

    $w = Open-Tool "merge-pdfs" @("$inputs\sample.pdf", "$inputs\second.pdf")
    Check-Run "merge-pdfs" $w (Run-Tool $w) "15-merge-pdfs"

    $w = Open-Tool "split-pdf" @("$inputs\sample.pdf")
    Check-Run "split-pdf" $w (Run-Tool $w) "16-split-pdf"

    $w = Open-Tool "redact-pdf" @("$inputs\sample.pdf")
    Set-Text $w $A::NameProperty "Words or phrases to redact" "Jane Doe`r`n4111-2222-3333"
    Set-Toggle $w "Redact email addresses"
    Set-Toggle $w "Redact phone numbers"
    Check-Run "redact-pdf" $w (Run-Tool $w) "17-redact-pdf"

    $w = Open-Tool "transcribe" @("$inputs\speech.mp3")
    Save-Shot $w "18-transcribe-before"
    Check-Run "transcribe" $w (Run-Tool $w 900) "18-transcribe"

    $w = Open-Tool "make-qr-code" @()
    Set-Text $w $A::AutomationIdProperty "TextInput" "https://github.com/kenhendricks00"
    Check-Run "make-qr-code" $w (Run-Tool $w) "19-make-qr-code"

    $qr = Get-ChildItem $outputs -Recurse -Filter "QR code.png" | Select-Object -First 1
    $w = Open-Tool "read-qr-code" @($qr.FullName)
    Check-Run "read-qr-code" $w (Run-Tool $w) "20-read-qr-code"
    $decoded = (Find $w $A::AutomationIdProperty "Messages").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($decoded -ne "https://github.com/kenhendricks00") { throw "QR read back '$decoded'" }

    (Find $main $A::AutomationIdProperty "SettingsButton").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Save-Shot $main "30-settings"
}
finally {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    Remove-Item Env:\WINDOWS_TOOLBOX_DATA
}

$after = Get-ChildItem $inputs | ForEach-Object { "$($_.Name)=$((Get-FileHash $_.FullName).Hash)" }
if (Compare-Object $before $after) { throw "Input files changed!" }
Write-Host "inputs unchanged     $($before.Count) files, same SHA-256"
Write-Host ""
Get-ChildItem $outputs -Recurse -File | ForEach-Object { "{0,10:N0}  {1}" -f $_.Length, $_.FullName.Substring($outputs.Length + 1) }
