#requires -Version 5.1
<#
.SYNOPSIS
    Publish Boot Scanner as a single self-contained EXE for 64-bit Windows 10/11.

.DESCRIPTION
    Requires the .NET 8 SDK. Does not require TwinCAT or Visual Studio.
    Output:  <repo>\publish\win-x64\BootScanner.exe

    ReadyToRun is enabled to speed up first start. Trimming is OFF by default
    because WinForms reflection is easy to break with the trimmer.
    To experiment with trimming (not recommended for v1):

        .\build-windows.ps1 -Trim
#>
[CmdletBinding()]
param(
    [switch] $Trim,
    [switch] $NoReadyToRun,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Project = Join-Path $Root "src\BootScanner.App\BootScanner.App.csproj"
$OutDir = Join-Path $Root "publish\win-x64"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "未找到 dotnet。请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0"
}

Write-Host "Publishing BootScanner (win-x64, self-contained, single file)..."
Write-Host "  Project : $Project"
Write-Host "  Output  : $OutDir"

$props = @(
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:PublishReadyToRun=$([bool](-not $NoReadyToRun))",
    "-p:PublishTrimmed=$([bool]$Trim)",
    "-o", $OutDir
)

dotnet publish $Project @props
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exe = Join-Path $OutDir "BootScanner.exe"
if (Test-Path $exe) {
    $info = Get-Item $exe
    Write-Host ""
    Write-Host "OK  $($info.FullName)"
    Write-Host "    Size: $([math]::Round($info.Length / 1MB, 1)) MB"
    Write-Host ""
    Write-Host "Copy BootScanner.exe to any Win10/Win11 x64 machine. TwinCAT is not required."
} else {
    Write-Error "Publish finished but BootScanner.exe was not found in $OutDir"
}
