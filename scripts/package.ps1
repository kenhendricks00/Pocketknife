<#
.SYNOPSIS
Publishes a self-contained x64 build and writes the release artifacts to release/:
a portable zip, an installer (when Inno Setup's ISCC.exe is available) and SHA256SUMS.txt.
#>
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$publish = Join-Path $root "artifacts\publish"
$release = Join-Path $root "release"
$name = "Pocketknife-$version-win-x64"

Remove-Item $publish, $release -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $release | Out-Null

dotnet publish (Join-Path $root "src\Pocketknife.App") -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$zip = Join-Path $release "$name-portable.zip"
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal

if (-not $SkipInstaller) {
    $iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it or pass -SkipInstaller." }
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$release" (Join-Path $root "installer\Pocketknife.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
}

# LF line endings so `sha256sum -c SHA256SUMS.txt` works in Git Bash, WSL and Linux, not just PowerShell.
$sums = Get-ChildItem $release -File | Where-Object Name -ne "SHA256SUMS.txt" | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)`n"
}
[IO.File]::WriteAllText((Join-Path $release "SHA256SUMS.txt"), -join $sums, [Text.Encoding]::ASCII)

Get-ChildItem $release -File | Format-Table Name, @{ n = "MB"; e = { "{0:N1}" -f ($_.Length / 1MB) } }
