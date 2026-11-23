# Melvor2Patch

基于 [Harmony](https://github.com/pardeike/Harmony) 的 **Melvor Idle 2** 游戏内修改器。所有功能都尽量走游戏自己的逻辑（保留判定、离线进度、官方 API），不直接篡改数值，统计数据保持合理。

> 本项目与游戏开发商 Games By Malcs 无关，使用风险自负。修改器的操作会直接写入存档，建议先备份存档。

## 功能

| 功能 | 说明 |
|---|---|
| God Mode | 伤害照常结算并计入统计，但 HP 在扣血后立即还原，不会死亡、不触发死亡惩罚和自动进食。覆盖战斗、偷窃、敏捷。 |
| Time Skip | 跳过 0.5–24 小时，由游戏原版的离线进度流程结算（同官方调试菜单的 Run Offline）。 |
| No Consumption | 24 个可单独开关的“不消耗”：弹药、施法/附魔符文、祈祷点、食物、工具、装备、钓鱼/狩猎鱼饵、陷阱、敏捷入场费、各制造技能原料、种子、堆肥、农场自动化、磨坊、牧场饲料。仍需拥有材料，只是不扣除。 |
| Item Spawner | 按名称或 ID 搜索物品并添加到银行；添加任意货币。 |
| Official Debug Menu | 打开游戏发布版中保留但没有入口的官方调试菜单。 |

游戏内的修改器窗口可拖动、可折叠；所有开关自动保存。

## 安装（普通用户）

1. 从 [Releases](../../releases) 下载 `Melvor2Patch-vX.Y.Z.zip` 并解压。
2. 关闭游戏，双击 `Melvor2Injector.exe`，选择 **[1] 安装 / 更新**。
   - 通过 Steam 自动查找游戏；找不到时按提示把游戏文件夹拖入窗口。
   - 自带运行时，无需安装 .NET。
3. 启动游戏，屏幕左侧出现 Melvor2Patch 窗口即成功。

**游戏更新后**：Steam 更新或“验证游戏文件完整性”会还原游戏文件，重新运行安装器选择 [1] 即可，设置会保留。
**卸载**：运行安装器选择 [2]，或在 Steam 中验证游戏文件完整性。

## 工作原理

- **Melvor2Injector** 用 Mono.Cecil 在游戏程序集 `GamesByMalcs.Melvor2.dll` 的 Godot 入口 `GodotPlugins.Game.Main.InitializeFromGameProject` 末尾插入一段引导代码（外层带 try/catch，mod 出错不影响游戏启动），原版文件备份为 `.dll.bak`。
- 引导代码把 `<游戏目录>\Melvor2Patch\Melvor2Patch.dll` 加载进**游戏自己的 AssemblyLoadContext**（Godot 通过 hostfxr 组件方式加载游戏，不在 Default ALC），保证 mod 与游戏共享同一份类型，然后应用 Harmony 补丁。
- 引导阶段 Godot 原生层尚未完全就绪，此时只应用补丁；UI 在 `Main._Ready` 之后创建。

## 从源码构建

需要：Windows、[.NET 10 SDK](https://dotnet.microsoft.com/download)。本机装有 Melvor Idle 2 时编译引用真实的游戏程序集；没有安装时自动改用 `lib/GameRefs/` 中的引用程序集存根。

```powershell
# 开发：构建 + 部署 mod 到游戏目录 + 注入（需要本机装有游戏）
powershell -ExecutionPolicy Bypass -File .\scripts\dev-deploy.ps1

# 打包发布：生成 dist\Melvor2Patch-vX.Y.Z.zip
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

游戏目录按以下优先级确定：`-GameDir` / `-p:GameDir=...` 参数 → `Directory.Build.local.props`（复制 `.example` 后修改，不进版本库）→ Steam 注册表路径 + `steamapps\common\Melvor Idle 2`。

Injector 命令行：`Melvor2Injector [install|uninstall|status|patch|restore] [--game <路径>] [--dry-run]`，详见 `Melvor2Injector help`。

### 发布新版本

发布由 GitHub Actions（`.github/workflows/release.yml`）自动完成：修改 `Directory.Build.props` 中的 `<Version>` 并 push 到 `main`，若 tag `v<版本>` 尚不存在，就会自动编译、打包并创建 Release（附带自动生成的更新说明）。版本号含 `-`（如 `0.3.0-beta.1`）时发布为 Pre-release。版本号未变的 push 与 PR 只做构建检查。

### 游戏引用存根（lib/GameRefs）

云端没有游戏，仓库也不包含任何游戏文件。`lib/GameRefs/` 中是用 [JetBrains Refasmer](https://github.com/JetBrains/Refasmer) 生成的**引用程序集**：只保留公开类型与方法签名，不含任何方法体代码和私有/内部类型，无法运行，仅用于编译。来源版本记录在 `lib/GameRefs/SOURCE.txt`。

游戏更新后如果 mod 用到的 API 有变化（本机编译失败或需要新的游戏类型），在装有游戏的机器上运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\update-game-refs.ps1   # 然后提交 lib/GameRefs/
```

### 目录结构

```
src/Melvor2Patch/       mod 本体（Core 基础设施 / Features 各功能 / UI 窗口）
src/Melvor2Injector/    安装器与注入器
lib/Harmony/            0Harmony.dll（MIT）
lib/GameRefs/           游戏引用程序集存根（仅签名，用于无游戏环境编译）
scripts/                开发部署、打包、更新存根脚本
packaging/              发布包附带的说明与第三方许可
.github/workflows/      自动构建与发布
```

### 调试提示

- mod 日志：`<游戏目录>\Melvor2Patch\Melvor2Patch.log`；游戏日志：`%APPDATA%\Games By Malcs\Melvor Idle 2\logs\`。
- 在 mod 目录放一个空文件 `dump-metadata.flag`，下次启动会把游戏程序集的真实类型/成员（含可访问性）导出到 `Melvor2Patch\dump\`，用于确认补丁目标。
- 绕过 Steam 直接启动游戏时需设置环境变量 `SteamAppId=3218350` 与 `SteamGameId=3218350`，否则游戏会通过 Steam 重启。

## 许可

[MIT](LICENSE)。第三方组件见 [packaging/THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt)。
