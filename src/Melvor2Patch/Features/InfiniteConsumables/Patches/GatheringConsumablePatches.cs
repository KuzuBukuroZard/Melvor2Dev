using System.Reflection;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Entities.Agility;
using GamesByMalcs.Melvor2.Entities.Fishing;
using GamesByMalcs.Melvor2.Entities.Hunting;
using HarmonyLib;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.InfiniteConsumables.Patches;

/// <summary>采集与敏捷类消耗：工具、装备、钓鱼鱼饵、狩猎鱼饵、陷阱、敏捷入场费。</summary>
internal static class GatheringConsumablePatches
{
    /// <summary>工具栏物品的“激活时消耗”。</summary>
    [HarmonyPatch]
    private static class Tools
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveToolbeltSlot), "EquippedConsumesOnActivatedHandler");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.Tools);
    }

    /// <summary>装备栏物品的“激活时消耗”。</summary>
    [HarmonyPatch]
    private static class Equipment
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveEquipmentSlot), "EquippedConsumesOnActivatedHandler");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.Equipment);
    }

    /// <summary>
    /// 钓鱼鱼饵。原版在保留判定成功后仍会继续扣除鱼饵（缺少 return），保留判定实际不起作用，
    /// 因此开启时直接跳过整次扣除。鱼饵加成按库存数量生效，不受影响。
    /// </summary>
    [HarmonyPatch]
    private static class FishingBait
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(FishingBaitComponent), "ConsumeSelectedBait");

        private static bool Prefix() => !InfiniteConsumables.IsEnabled(ConsumableCategory.FishingBait);
    }

    /// <summary>
    /// 狩猎鱼饵。原版没有保留判定，且 BaitConsumed 会参与后续的捕获结算，
    /// 因此开启时按原版语义设置 BaitConsumed（库存里有鱼饵即为成功），只是不扣库存。
    /// </summary>
    [HarmonyPatch]
    private static class HuntingBait
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveHuntingAnimal), "ConsumeBait");

        private static bool Prefix(ActiveHuntingAnimal __instance, ref bool __result)
        {
            if (!InfiniteConsumables.IsEnabled(ConsumableCategory.HuntingBait)
                || !__instance.UseBait
                || __instance.Animal.PreferredBait is not { } bait)
            {
                return true;
            }

            bool hasBait = __instance.Component.Inventory.GetQuantity(bait) > 0;
            SetBaitConsumed(__instance, hasBait);
            __result = hasBait;
            return false;
        }

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_BaitConsumed")]
        private static extern void SetBaitConsumed(ActiveHuntingAnimal animal, bool value);
    }

    /// <summary>狩猎陷阱。同一方法里的捕获成功率判定（TrappingSuccessChance）不受影响。</summary>
    [HarmonyPatch]
    private static class Traps
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveHuntingAnimal), nameof(ActiveHuntingAnimal.AttemptToTrap));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.Traps);
    }

    /// <summary>敏捷路线的入场费（EntryCostPreservationChance 判定）。入场前的费用校验保持原样。</summary>
    [HarmonyPatch]
    private static class AgilityEntry
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveAgilityCoursePath), nameof(ActiveAgilityCoursePath.Start));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.AgilityEntry);
    }
}
