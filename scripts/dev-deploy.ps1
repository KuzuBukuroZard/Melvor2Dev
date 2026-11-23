<#
.SYNOPSIS
    开发用：检查游戏是否在运行 → 构建解决方案（自动部署 mod 到游戏目录）→ 注入加载入口 → 显示状态。
    修改代码后，或游戏更新 / 在 Steam 中验证完整性之后运行。

.EXAMPLE
    .\scripts\dev-deploy.ps1
    .\scripts\dev-deploy.ps1 -DryRun                    # 只校验能否注入，不写入游戏程序集（构建仍会部署 mod）
    .\scripts\dev-deploy.ps1 -Force                     # 修改了 Injector 的注入代码后，从备份重新注入
    .\scripts\dev-deploy.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Melvor Idle 2"
#>
[CmdletBinding()]
param(
    # 游戏根目录；不指定时由 Directory.Build.props 决定（local.props / Steam 注册表）
    [string]$GameDir,

    [switch]$Force,

    [switch]$DryRun,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root 'Melvor2Patch.slnx'
$injectorProject = Join-Path $root 'src\Melvor2Injector\Melvor2Injector.csproj'

# 在命令行参数里，路径末尾的反斜杠会转义结尾引号，这里统一去掉
if ($GameDir) { $GameDir = $GameDir.TrimEnd('\', '/') }

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Stop-WithError([string]$text) {
    Write-Host ''
    Write-Host "失败：$text" -ForegroundColor Red
    exit 1
}

Write-Step '检查游戏是否在运行'
$running = Get-Process -Name 'Melvor2' -ErrorAction SilentlyContinue
if ($running) { Stop-WithError "游戏正在运行（PID $($running.Id -join ', ')），请先关闭游戏。" }
Write-Host '游戏未运行。'

Write-Step "构建解决方案（$Configuration）"
$buildArgs = @('build', $solution, '-c', $Configuration, '-nologo', '-v', 'minimal')
if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { Stop-WithError '构建失败，请查看上面的编译错误。' }

# 用 dotnet run 调用 Injector，不依赖具体输出路径；--no-build 复用上一步的构建结果
function Invoke-Injector([string[]]$injectorArgs) {
    if ($GameDir) { $injectorArgs += @('--game', $GameDir) }
    & dotnet run --project $injectorProject -c $Configuration --no-build -- @injectorArgs
}

Write-Step '注入加载入口'
$patchArgs = @('patch')
if ($Force) { $patchArgs += '--force' }
if ($DryRun) { $patchArgs += '--dry-run' }
Invoke-Injector $patchArgs
if ($LASTEXITCODE -ne 0) { Stop-WithError '注入失败，请查看上面的错误信息。' }

Write-Step '当前状态'
Invoke-Injector @('status')

Write-Host ''
Write-Host '完成。启动游戏后可在 <游戏根目录>\Melvor2Patch\Melvor2Patch.log 中确认补丁应用情况。' -ForegroundColor Green
