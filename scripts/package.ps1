<#
.SYNOPSIS
    打包 win-x64 自包含产物为带版本号的 zip，并生成 SHA256 校验文件。
.DESCRIPTION
    默认先调用 publish.ps1 生成 artifacts/publish，再整理成
    artifacts/package/Bd.Ui-<version>-win-x64/ 并压缩；-SkipBuild 复用已有发布目录。
.EXAMPLE
    pwsh scripts/package.ps1
.EXAMPLE
    pwsh scripts/package.ps1 -SkipBuild
.EXAMPLE
    pwsh scripts/package.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$PublishDir = '',
    [string]$Output = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$props = Join-Path $root 'Directory.Build.props'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $match = [regex]::Match((Get-Content -LiteralPath $props -Raw), '<Version>([^<]+)</Version>')
    if (-not $match.Success) {
        throw '无法从 Directory.Build.props 读取版本号，请显式传入 -Version'
    }

    $Version = $match.Groups[1].Value.Trim()
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "版本号格式不合法：$Version"
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $root 'artifacts/publish'
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'artifacts/package'
}

$exe = Join-Path $PublishDir 'Bd.Ui.exe'
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'publish.ps1') -Configuration $Configuration -Runtime $Runtime -Output $PublishDir
    if (-not (Test-Path $exe)) {
        throw "发布失败：缺少 $exe"
    }
}
elseif (-not (Test-Path $exe)) {
    throw "-SkipBuild 需要已有发布产物：$exe"
}

$name = "Bd.Ui-$Version-$Runtime"
$stageRoot = Join-Path $Output $name
$zip = Join-Path $Output "$name.zip"

if (Test-Path $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $stageRoot -Recurse -Force

$readme = @"
Bd.Ui $Version ($Runtime)

- 自包含单文件，目标机器无需安装 .NET 运行时。
- 直接运行 Bd.Ui.exe；首次启动若未找到 bd，会自动进入只读演示模式。
- 配置文件：%APPDATA%\Bd.Ui\config.json
- 日志目录：%APPDATA%\Bd.Ui\logs
"@
[System.IO.File]::WriteAllText(
    (Join-Path $stageRoot 'README.txt'),
    $readme,
    [System.Text.UTF8Encoding]::new($false))

Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$shaFile = "$zip.sha256"
[System.IO.File]::WriteAllText(
    $shaFile,
    "$hash  $(Split-Path -Leaf $zip)`n",
    [System.Text.UTF8Encoding]::new($false))

$sizeMb = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 2)

Write-Host ""
Write-Host "打包完成" -ForegroundColor Green
Write-Host "  版本：$Version"
Write-Host "  ZIP ：$zip ($sizeMb MB)"
Write-Host "  SHA ：$shaFile"
Write-Host "  摘要：$hash"
