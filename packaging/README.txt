Melvor2Patch - Melvor Idle 2 修改器
====================================

安装
----
1. 关闭游戏。
2. 解压整个压缩包（Melvor2Injector.exe 必须和 Melvor2Patch 文件夹放在一起）。
3. 双击 Melvor2Injector.exe，选择 [1] 安装 / 更新。
   - 会自动通过 Steam 找到游戏；找不到时按提示把游戏文件夹拖进窗口即可。
   - 不需要安装 .NET。
4. 启动游戏，屏幕左侧出现 Melvor2Patch 小窗口即安装成功。

游戏更新后
----------
Steam 更新游戏（或"验证游戏文件完整性"）会还原游戏文件，修改器随之失效。
重新运行 Melvor2Injector.exe 选择 [1] 即可，你的设置会保留。

卸载
----
运行 Melvor2Injector.exe 选择 [2]。也可以在 Steam 中"验证游戏文件完整性"。

文件位置
--------
<游戏目录>\Melvor2Patch\           mod 文件、设置（settings.json）、日志（Melvor2Patch.log）
<游戏目录>\data_...\GamesByMalcs.Melvor2.dll.bak   安装时自动备份的原版文件

注意
----
- 本工具与游戏开发商无关，使用风险自负。修改器中的操作（刷物品、跳过时间等）会直接写入存档，
  建议使用前备份存档。
- Windows 可能提示"未知发布者"，这是因为 exe 没有数字签名；源码可在项目主页审阅。
- 遇到问题请附上 <游戏目录>\Melvor2Patch\Melvor2Patch.log。
