param(
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDirectory = Join-Path $root 'src'
$outputPath = Join-Path $root 'FpsSocdTool.exe'
$iconPath = Join-Path $root 'app.ico'

$cscCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw '找不到 .NET Framework 4.x 的 csc.exe，无法编译。'
}

if (-not (Test-Path -LiteralPath $iconPath)) {
    & (Join-Path $root 'tools\make-icon.ps1')
}

$sourceFiles = Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $sourceFiles -or $sourceFiles.Count -eq 0) {
    throw 'src 目录中没有 .cs 源文件。'
}

$compilerArguments = @(
    '/nologo',
    '/target:winexe',
    '/codepage:65001',
    '/optimize+',
    '/platform:anycpu',
    "/win32icon:$iconPath",
    "/win32manifest:$(Join-Path $sourceDirectory 'app.manifest')",
    "/out:$outputPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $sourceFiles

& $csc @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，csc 退出码：$LASTEXITCODE"
}

Write-Host "编译成功：$outputPath"

if ($SelfTest) {
    $report = Join-Path $env:TEMP 'FpsSocdTool-selftest.txt'
    $reportArgument = '--report="' + $report + '"'
    $process = Start-Process -FilePath $outputPath -ArgumentList @('--selftest', $reportArgument) -Wait -PassThru
    Write-Host "自测退出码：$($process.ExitCode)"
    if (Test-Path -LiteralPath $report) {
        Get-Content -LiteralPath $report -Encoding UTF8
    }
    if ($process.ExitCode -ne 0) {
        throw '自测失败。'
    }
}
