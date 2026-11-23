using GamesByMalcs.Melvor2;
using HarmonyLib;

namespace Melvor2Patch.Core;

/// <summary>把游戏的生命周期节点接到 mod 的阶段回调上。</summary>
internal static class LifecyclePatches
{
    /// <summary>
    /// 主节点 Main 就绪时进入游戏就绪阶段（见 <see cref="Entry.OnMainReady"/>）。
    /// Main._Ready 是 async void，Postfix 会在其第一个 await 返回时执行。
    /// </summary>
    [HarmonyPatch(typeof(Main), nameof(Main._Ready))]
    private static class MainReady
    {
        private static void Postfix(Main __instance) => Entry.OnMainReady(__instance);
    }
}
