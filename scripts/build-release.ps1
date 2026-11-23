<#
.SYNOPSIS
    打包发布：生成可直接分发给普通用户的 zip（无需安装 .NET）。

    dist\Melvor2Patch-v<版本>.zip
      Melvor2Injector.exe        单文件、自带运行时
      Melvor2Patch\              Melvor2Patch.dll、0Harmony.dll
      README.txt                 使用说明
      LICENSE.txt / THIRD-PARTY-NOTICES.txt

    编译 mod 需要本机安装了游戏（引用游戏程序集），见 Directory.Build.props。

.EXAMPLE
    .\scripts\build-release.ps1
    .\scripts\build-release.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Melvor Idle 2"
#>
[CmdletBinding()]
param(
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
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

# 版本号取自 Directory.Build.props
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$name = "Melvor2Patch-v$version"
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
$zip = Join-Path $dist "$name.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Melvor2Patch') | Out-Null

$gameArg = @(); if ($GameDir) { $gameArg = @("-p:GameDir=$GameDir") }

Write-Step "编译 mod（Release，v$version）"
& dotnet build (Join-Path $root 'src\Melvor2Patch\Melvor2Patch.csproj') -c Release -nologo -v minimal -p:DeployToGame=false @gameArg
if ($LASTEXITCODE -ne 0) { Stop-WithError '编译 mod 失败。' }

Write-Step '发布 Injector（单文件、自带运行时）'
& dotnet publish (Join-Path $root 'src\Melvor2Injector\Melvor2Injector.csproj') -c Release -nologo -v minimal -o (Join-Path $dist 'injector-publish')
if ($LASTEXITCODE -ne 0) { Stop-WithError '发布 Injector 失败。' }

Write-Step '组装发布包'
$modOut = Join-Path $root 'src\Melvor2Patch\bin\Release\net10.0'
Copy-Item (Join-Path $modOut 'Melvor2Patch.dll') (Join-Path $stage 'Melvor2Patch')
Copy-Item (Join-Path $root 'lib\Harmony\0Harmony.dll') (Join-Path $stage 'Melvor2Patch')
Copy-Item (Join-Path $dist 'injector-publish\Melvor2Injector.exe') $stage
Copy-Item (Join-Path $root 'packaging\README.txt') $stage
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $root 'packaging\THIRD-PARTY-NOTICES.txt') $stage
Remove-Item (Join-Path $dist 'injector-publish') -Recurse -Force

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Get-ChildItem $stage -Recurse -File | ForEach-Object { "{0,10:N0}  {1}" -f $_.Length, $_.FullName.Substring($stage.Length + 1) }

Write-Host ''
Write-Host "完成：$zip（$([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB）" -ForegroundColor Green
