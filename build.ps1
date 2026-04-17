$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$outputDir = Join-Path $repoRoot "dist"

$possibleCompilers = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
)

$compiler = $possibleCompilers | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) {
    throw "Kein passender C#-Compiler gefunden. Erwartet wurde Roslyn aus Visual Studio 2022."
}

$frameworkDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path $frameworkDir)) {
    $frameworkDir = "C:\Windows\Microsoft.NET\Framework\v4.0.30319"
}

if (-not (Test-Path $frameworkDir)) {
    throw "Das .NET-Framework-Referenzverzeichnis wurde nicht gefunden."
}

New-Item -ItemType Directory -Force $outputDir | Out-Null

& $compiler `
    /nologo `
    /target:winexe `
    /langversion:latest `
    /nullable:enable `
    /optimize+ `
    /out:"$outputDir\OpenRoadTyper.exe" `
    /r:"$frameworkDir\System.Windows.Forms.dll" `
    /r:"$frameworkDir\System.Drawing.dll" `
    "$repoRoot\Program.cs" `
    "$repoRoot\MainForm.cs"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Build abgeschlossen: $outputDir\OpenRoadTyper.exe"
