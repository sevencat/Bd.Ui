<#
.SYNOPSIS
    发布 Bd.Ui 为 win-x64 自包含单文件。
.DESCRIPTION
    产物默认落在 artifacts/publish，理论上只有 Bd.Ui.exe（Avalonia 平台链与 SkiaSharp/HarfBuzzSharp
    都由 Win64Aio 包提供，单文件发布时一并打进 exe）。若出现额外文件会给出警告。
.EXAMPLE
    pwsh scripts/publish.ps1
.EXAMPLE
    pwsh scripts/publish.ps1 -Output D:\dist
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Bd.App/Bd.App.csproj'

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'artifacts/publish'
}

if (-not (Test-Path $project)) {
    throw "找不到工程：$project"
}

if (Test-Path $Output) {
    Remove-Item -Recurse -Force $Output
}

Write-Host "发布 $project → $Output ($Configuration/$Runtime)" -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $Output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（exit $LASTEXITCODE）"
}

$files = Get-ChildItem $Output -File
$files | Select-Object Name, @{ Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize

$unexpected = $files | Where-Object { $_.Extension -ne '.exe' }
if ($unexpected) {
    Write-Warning ('发布目录存在非 exe 文件：' + ($unexpected.Name -join ', '))
}

$exe = Join-Path $Output 'Bd.Ui.exe'
if (-not (Test-Path $exe)) {
    throw "发布目录缺少 Bd.Ui.exe"
}

Write-Host "完成：$exe" -ForegroundColor Green
