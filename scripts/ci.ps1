<#
.SYNOPSIS
    本地与 GitHub Actions 共用的质量门禁：restore → build(warnaserror) → test → publish 冒烟。
.DESCRIPTION
    失败立即抛错并以非零退出码结束；发布冒烟通过 BD_UI_SMOKE_EXIT_SECONDS
    让应用自动正常退出，避免残留 GUI 进程。
.EXAMPLE
    pwsh scripts/ci.ps1
.EXAMPLE
    pwsh scripts/ci.ps1 -SkipSmoke
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipRestore,
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Bd.Ui.slnx'

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 失败（exit $LASTEXITCODE）"
    }
}

Push-Location $root
try {
    if (-not $SkipRestore) {
        Invoke-Step 'restore' { dotnet restore $solution }
    }

    Invoke-Step 'build (warnaserror)' {
        dotnet build $solution -c $Configuration --no-restore -warnaserror
    }

    Invoke-Step 'test' {
        dotnet test $solution -c $Configuration --no-build
    }

    if (-not $SkipSmoke) {
        $publishDir = Join-Path $root 'artifacts/ci-publish'
        Invoke-Step 'publish' {
            & (Join-Path $PSScriptRoot 'publish.ps1') -Configuration $Configuration -Output $publishDir
        }

        Invoke-Step 'publish 冒烟' {
            $env:BD_UI_SMOKE_EXIT_SECONDS = '6'
            try {
                $process = Start-Process -FilePath (Join-Path $publishDir 'Bd.Ui.exe') -PassThru
                if (-not $process.WaitForExit(30000)) {
                    try { $process.Kill($true) } catch { }
                    throw 'publish 冒烟超时（30 秒未退出）'
                }

                if ($process.ExitCode -ne 0) {
                    throw "publish 冒烟退出码 $($process.ExitCode)"
                }

                Write-Host '应用启动并在 6 秒后正常退出' -ForegroundColor DarkGray
            }
            finally {
                Remove-Item Env:BD_UI_SMOKE_EXIT_SECONDS -ErrorAction SilentlyContinue
            }

            $global:LASTEXITCODE = 0
        }
    }

    Write-Host ""
    Write-Host '质量门禁全部通过' -ForegroundColor Green
}
finally {
    Pop-Location
}
