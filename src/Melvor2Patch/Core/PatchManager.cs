using HarmonyLib;

namespace Melvor2Patch.Core;

/// <summary>
/// Harmony 补丁管理。逐个应用 mod 程序集中标记了 [HarmonyPatch] 的类（含嵌套类），
/// 单个补丁类失败只记录日志，不影响其他补丁和游戏启动。
/// </summary>
internal static class PatchManager
{
    private static Harmony? _harmony;

    public static void ApplyAll()
    {
        _harmony ??= new Harmony(ModContext.ModId);

        int succeeded = 0;
        int failed = 0;
        foreach (Type type in AccessTools.GetTypesFromAssembly(ModContext.ModAssembly))
        {
            if (!type.IsDefined(typeof(HarmonyPatch), inherit: false))
            {
                continue;
            }

            try
            {
                _harmony.CreateClassProcessor(type).Patch();
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                ModLog.Error($"补丁应用失败：{type.FullName}", ex);
            }
        }

        ModLog.Info($"补丁应用完成：成功 {succeeded}，失败 {failed}。");
    }
}
