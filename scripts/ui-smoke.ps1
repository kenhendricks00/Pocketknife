<#
.SYNOPSIS
Drives the real app through UI Automation: opens every tool, runs it on the test fixtures, checks the result and
saves a screenshot of each screen. Also presses the global launcher hotkey and checks the launcher appears.

.PARAMETER Exe
Path to Pocketknife.exe.
.PARAMETER Work
Scratch folder, new or made by an earlier run of this script. Sample inputs go in Samples\, results in
Downloads\Pocketknife\ and the app's data in data\, so your own settings and Downloads are untouched.
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
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class Native {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    // inset trims a window's transparent margin (the launcher draws its card 16 px inside its window).
    public static Bitmap Capture(IntPtr hwnd, int inset, int radius) {
        SetForegroundWindow(hwnd);
        System.Threading.Thread.Sleep(400);
        RECT r; DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT)));
        double scale = GetDpiForWindow(hwnd) / 96.0;
        inset = (int)Math.Round(inset * scale);
        radius = (int)Math.Round(radius * scale);
        var bmp = new Bitmap(r.Right - r.Left - 2 * inset, r.Bottom - r.Top - 2 * inset);
        using (var g = Graphics.FromImage(bmp)) { g.CopyFromScreen(r.Left + inset, r.Top + inset, 0, 0, bmp.Size); }
        RoundCorners(bmp, radius);
        return bmp;
    }
    // Windows 11 windows have rounded corners; make the pixels outside them transparent (antialiased).
    static void RoundCorners(Bitmap bmp, int radius) {
        for (int y = 0; y < radius; y++)
        for (int x = 0; x < radius; x++) {
            double dx = radius - x - 0.5, dy = radius - y - 0.5;
            double coverage = Math.Max(0, Math.Min(1, radius - Math.Sqrt(dx * dx + dy * dy) + 0.5));
            foreach (var p in new[] { new Point(x, y), new Point(bmp.Width - 1 - x, y), new Point(x, bmp.Height - 1 - y), new Point(bmp.Width - 1 - x, bmp.Height - 1 - y) }) {
                var c = bmp.GetPixel(p.X, p.Y);
                bmp.SetPixel(p.X, p.Y, Color.FromArgb((int)Math.Round(c.A * coverage), c.R, c.G, c.B));
            }
        }
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
$outputs = Join-Path $Work "Downloads\Pocketknife"
$inputs = Join-Path $Work "Samples"
$marker = Join-Path $Work ".pocketknife-smoke"
if ((Test-Path $Work) -and -not (Test-Path $marker)) { throw "$Work exists and was not created by this script. Choose a new folder." }
Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $data, $outputs, $inputs, $Screenshots | Out-Null
New-Item -ItemType File $marker | Out-Null
$fixtures = Join-Path $PSScriptRoot "..\tests\Pocketknife.Tests\Fixtures"
$samples = @{ "sample.mp4" = "holiday-clip.mp4"; "sample.pdf" = "invoice.pdf"; "speech.mp3" = "voice-note.mp3"; "subject.png" = "product-photo.png" }
foreach ($name in $samples.Keys) { Copy-Item (Join-Path $fixtures $name) (Join-Path $inputs $samples[$name]) }
Copy-Item (Join-Path $inputs "invoice.pdf") (Join-Path $inputs "receipt.pdf")
if ($EngineCache) { Copy-Item $EngineCache (Join-Path $data "engines") -Recurse }
@{ OutputRoot = $outputs; Theme = $Theme; KeepRunningInTray = $true; LauncherHotkey = "Ctrl+Alt+Space" } |
    ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $data "settings.json")
$before = Get-ChildItem $inputs | ForEach-Object { "$($_.Name)=$((Get-FileHash $_.FullName).Hash)" }

# A plain backdrop behind the app, so the translucent window border and rounded corners never pick up
# other windows or the desktop in the screenshots.
$backdrop = New-Object System.Windows.Forms.Form
$backdrop.FormBorderStyle = "None"
$backdrop.ShowInTaskbar = $false
$backdrop.StartPosition = "Manual"
$backdrop.Bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
$backdrop.BackColor = if ($Theme -eq "Dark") { [System.Drawing.Color]::FromArgb(32, 32, 32) } else { [System.Drawing.Color]::FromArgb(243, 243, 243) }
$backdrop.Show()
[System.Windows.Forms.Application]::DoEvents()

$env:POCKETKNIFE_DATA = $data
$process = Start-Process $Exe -PassThru

function Wait-For([scriptblock]$probe, [int]$seconds = 30, [string]$what = "condition") {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $value = & $probe
        if ($value) { return $value }
        [System.Windows.Forms.Application]::DoEvents()
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

function Save-Shot($element, [string]$name, [int]$inset = 0, [int]$radius = 8) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 600
    $bitmap = [Native]::Capture([IntPtr]$element.Current.NativeWindowHandle, $inset, $radius)
    $bitmap.Save((Join-Path $Screenshots "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "  screenshot $name.png"
}

function Open-Tool([string]$id, [string[]]$paths) {
    Start-Process $Exe -ArgumentList (@("--tool", $id) + ($paths | ForEach-Object { "`"$_`"" })) -Wait
    Start-Sleep -Milliseconds 800
    $window = Get-Window "Pocketknife"
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
    $main = Get-Window "Pocketknife"
    Save-Shot $main "01-home"

    [Native]::Chord(@(0x11, 0x12, 0x20))
    $launcher = Get-Window "Find a tool"
    Set-Text $launcher $A::AutomationIdProperty "LauncherQuery" "gif"
    Save-Shot $launcher "02-launcher" 17 9
    $first = (Find $launcher $A::AutomationIdProperty "LauncherResults").FindFirst($Tree::Children, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::ListItem)))
    if ($first.Current.Name -ne "Video to GIF") { throw "Launcher's first result was '$($first.Current.Name)'" }
    Write-Host "launcher hotkey      opened, 'gif' -> Video to GIF"
    [Native]::Chord(@(0x1B))

    $w = Open-Tool "compress-video" @("$inputs\holiday-clip.mp4")
    Check-Run "compress-video" $w (Run-Tool $w) "10-compress-video"

    $w = Open-Tool "convert-media" @("$inputs\holiday-clip.mp4")
    Check-Run "convert-media" $w (Run-Tool $w) "11-convert-media"

    $w = Open-Tool "video-to-gif" @("$inputs\holiday-clip.mp4")
    Check-Run "video-to-gif" $w (Run-Tool $w) "12-video-to-gif"

    $w = Open-Tool "download-video" @()
    Set-Text $w $A::AutomationIdProperty "TextInput" $VideoUrl
    Check-Run "download-video" $w (Run-Tool $w) "13-download-video"

    $w = Open-Tool "remove-background" @("$inputs\product-photo.png")
    Check-Run "remove-background" $w (Run-Tool $w) "14-remove-background"

    $w = Open-Tool "merge-pdfs" @("$inputs\invoice.pdf", "$inputs\receipt.pdf")
    Check-Run "merge-pdfs" $w (Run-Tool $w) "15-merge-pdfs"

    $w = Open-Tool "split-pdf" @("$inputs\invoice.pdf")
    Check-Run "split-pdf" $w (Run-Tool $w) "16-split-pdf"

    $w = Open-Tool "redact-pdf" @("$inputs\invoice.pdf")
    Set-Text $w $A::NameProperty "Words or phrases to redact" "Jane Doe`r`n4111-2222-3333"
    Set-Toggle $w "Redact email addresses"
    Set-Toggle $w "Redact phone numbers"
    Check-Run "redact-pdf" $w (Run-Tool $w) "17-redact-pdf"

    $w = Open-Tool "transcribe" @("$inputs\voice-note.mp3")
    Save-Shot $w "18-transcribe-before"
    Check-Run "transcribe" $w (Run-Tool $w 900) "18-transcribe"

    $w = Open-Tool "make-qr-code" @()
    Set-Text $w $A::AutomationIdProperty "TextInput" "https://github.com/kenhendricks00/Pocketknife"
    Check-Run "make-qr-code" $w (Run-Tool $w) "19-make-qr-code"

    $qr = Get-ChildItem $outputs -Recurse -Filter "QR code.png" | Select-Object -First 1
    $w = Open-Tool "read-qr-code" @($qr.FullName)
    Check-Run "read-qr-code" $w (Run-Tool $w) "20-read-qr-code"
    $decoded = (Find $w $A::AutomationIdProperty "Messages").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($decoded -ne "https://github.com/kenhendricks00/Pocketknife") { throw "QR read back '$decoded'" }

    (Find $main $A::AutomationIdProperty "SettingsButton").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Save-Shot $main "30-settings"
}
finally {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    $backdrop.Close()
    Remove-Item Env:\POCKETKNIFE_DATA
}

$after = Get-ChildItem $inputs | ForEach-Object { "$($_.Name)=$((Get-FileHash $_.FullName).Hash)" }
if (Compare-Object $before $after) { throw "Input files changed!" }
Write-Host "inputs unchanged     $($before.Count) files, same SHA-256"
Write-Host ""
Get-ChildItem $outputs -Recurse -File | ForEach-Object { "{0,10:N0}  {1}" -f $_.Length, $_.FullName.Substring($outputs.Length + 1) }
