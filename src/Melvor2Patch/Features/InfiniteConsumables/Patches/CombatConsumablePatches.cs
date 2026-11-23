using System.Reflection;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Components.MagicSpells;
using GamesByMalcs.Melvor2.Components.Prayers;
using HarmonyLib;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.InfiniteConsumables.Patches;

/// <summary>战斗类消耗：弹药、符文（施法 / 附魔）、祈祷点、食物。均为强制保留判定。</summary>
internal static class CombatConsumablePatches
{
    /// <summary>攻击后的弹药消耗（PlayerComponent.OnAttack 中的 AmmoPreservationChance 判定）。</summary>
    [HarmonyPatch]
    private static class Ammo
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(PlayerComponent), "OnAttack");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.Ammo);
    }

    /// <summary>攻击施法的符文消耗。施法前“符文是否足够”的校验保持原样。</summary>
    [HarmonyPatch]
    private static class SpellRunes
    {
        private static MethodBase TargetMethod() =>
            PatchTarget.Declared(typeof(MagicSpellsComponent), nameof(MagicSpellsComponent.TryConsumeAttackRuneCosts));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.SpellRunes);
    }

    /// <summary>附魔（Imbuement）的符文消耗。符文不足时附魔不生效的逻辑保持原样。</summary>
    [HarmonyPatch]
    private static class ImbuementRunes
    {
        private static MethodBase TargetMethod() =>
            PatchTarget.Declared(typeof(MagicSpellsComponent), nameof(MagicSpellsComponent.ConsumeImbuementCosts));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.ImbuementRunes);
    }

    /// <summary>祈祷点消耗。保留时游戏以 wasPreserved=true 发出事件，计入“祈祷点被保留”的统计。</summary>
    [HarmonyPatch]
    private static class Prayer
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActivePrayerSlot), "ActivatedConsumesOnBufferStartedHandler");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.Prayer);
    }

    /// <summary>手动进食与自动进食。回血与进食事件照常发生，只是不扣库存。</summary>
    [HarmonyPatch]
    private static class Food
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
        [
            PatchTarget.Declared(typeof(FoodComponent), nameof(FoodComponent.ManualEat)),
            PatchTarget.Declared(typeof(FoodComponent), "AutoEat"),
        ];

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.Food);
    }
}
