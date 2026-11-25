<#
.SYNOPSIS
    写入产品版本号（会进入 assembly 与关于页）。
.DESCRIPTION
    修改根目录 Directory.Build.props 中的 <Version>，下一次构建即写入
    AssemblyVersion / FileVersion / InformationalVersion。
.EXAMPLE
    pwsh scripts/set-version.ps1 0.2.0
.EXAMPLE
    pwsh scripts/set-version.ps1 0.2.0-preview.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "版本号格式应为 MAJOR.MINOR.PATCH 或 MAJOR.MINOR.PATCH-prerelease，实际：$Version"
}

$root = Split-Path -Parent $PSScriptRoot
$props = Join-Path $root 'Directory.Build.props'

if (-not (Test-Path $props)) {
    throw "找不到 $props"
}

$text = Get-Content -LiteralPath $props -Raw

if ($text -match '<Version>[^<]*</Version>') {
    $updated = $text -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
}
elseif ($text.Contains('<PropertyGroup>')) {
    $index = $text.IndexOf('<PropertyGroup>')
    $opening = '<PropertyGroup>'
    $updated = $text.Substring(0, $index) + $opening + "`r`n    <Version>$Version</Version>" + $text.Substring($index + $opening.Length)
}
else {
    throw 'Directory.Build.props 中既没有 <Version>，也没有 <PropertyGroup>，无法写入版本号'
}

[System.IO.File]::WriteAllText($props, $updated, [System.Text.UTF8Encoding]::new($false))

Write-Host "版本已写入：$Version" -ForegroundColor Green
Write-Host "  $props"
