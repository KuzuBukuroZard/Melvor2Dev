<#
.SYNOPSIS
    从本机安装的游戏生成编译用的引用程序集存根，写入 lib\GameRefs\（供 CI 在没有游戏的环境中编译 mod）。
    游戏更新且 mod 用到的 API 有变化时运行一次，然后提交 lib\GameRefs\。

    存根由 JetBrains Refasmer 生成：只保留公开类型与方法签名，不含任何方法体代码、私有/内部类型。

.EXAMPLE
    .\scripts\update-game-refs.ps1
    .\scripts\update-game-refs.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Melvor Idle 2"
#>
[CmdletBinding()]
param(
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$refDir = Join-Path $root 'lib\GameRefs'
$assemblies = @('GamesByMalcs.Melvor2.dll', 'GodotSharp.dll')

# 未指定时由 MSBuild 按 Directory.Build.props 的规则解析游戏目录（参数 → local.props → Steam 注册表）
if (-not $GameDir) {
    $GameDir = (& dotnet msbuild (Join-Path $root 'src\Melvor2Patch\Melvor2Patch.csproj') -nologo -getProperty:GameDir).Trim()
}
$dataDir = Join-Path $GameDir.TrimEnd('\', '/') 'data_GamesByMalcs.Melvor2_windows_x86_64'
foreach ($name in $assemblies) {
    if (-not (Test-Path (Join-Path $dataDir $name))) { throw "找不到 $name（游戏目录：$GameDir）。请用 -GameDir 指定。" }
}

Push-Location $root
try {
    & dotnet tool restore | Out-Null
    # Refasmer 目标框架较旧，允许在新版运行时上运行
    $env:DOTNET_ROLL_FORWARD = 'Major'
    $inputs = $assemblies | ForEach-Object { Join-Path $dataDir $_ }
    # -p：只保留公开 API；--omit-non-api-members true：去掉不参与公开 API 的私有成员与类型
    & dotnet refasmer -q -p --omit-non-api-members true -O $refDir @inputs 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Refasmer 执行失败。' }
}
finally {
    Remove-Item Env:DOTNET_ROLL_FORWARD -ErrorAction SilentlyContinue
    Pop-Location
}

# 记录来源版本，便于判断存根是否过期
$lines = @('# 由 scripts/update-game-refs.ps1 生成，只含公开 API 签名，不含任何方法体代码。', "generated: $(Get-Date -Format 'yyyy-MM-dd')")
foreach ($name in $assemblies) {
    $src = Get-Item (Join-Path $dataDir $name)
    $hash = (Get-FileHash $src.FullName -Algorithm SHA256).Hash.Substring(0, 16)
    $lines += "$name  version=$($src.VersionInfo.FileVersion)  source-sha256=$hash"
}
Set-Content -Path (Join-Path $refDir 'SOURCE.txt') -Value $lines -Encoding UTF8

Get-ChildItem $refDir | ForEach-Object { "{0,10:N0}  {1}" -f $_.Length, $_.Name }
Write-Host "完成：已更新 lib\GameRefs\，请提交这些文件。" -ForegroundColor Green
