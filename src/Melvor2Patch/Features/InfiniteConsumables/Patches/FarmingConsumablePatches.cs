using System.Reflection;
using System.Reflection.Emit;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Core.InventoryWalletTransactions;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Farming;
using GamesByMalcs.Melvor2.Entities.Ranching;
using HarmonyLib;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.InfiniteConsumables.Patches;

/// <summary>农牧类消耗：种子、堆肥、自动化费用、磨坊原料、牧场饲料。</summary>
internal static class FarmingConsumablePatches
{
    // ───────────── 有保留判定（Transpiler） ─────────────

    /// <summary>种植时的种子消耗（Plant 中的 PreservationChance 判定）。种植前的种子校验保持原样。</summary>
    [HarmonyPatch]
    private static class Seeds
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveFarmingPlot), nameof(ActiveFarmingPlot.Plant));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForCategory(instructions, original, ConsumableCategory.Seeds);
    }

    /// <summary>堆肥。额外传入 this（地块），仅在库存里确实有所选堆肥时才强制保留。</summary>
    [HarmonyPatch]
    private static class Compost
    {
        private static readonly MethodInfo ForceCompostMethod =
            AccessTools.Method(typeof(InfiniteConsumables), nameof(InfiniteConsumables.ForceCompostPreservation));

        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveFarmingPlot), nameof(ActiveFarmingPlot.Compost));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.Inject(instructions, original,
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, ForceCompostMethod));
    }

    // ───────────── 无保留判定（只校验、不扣除） ─────────────

    /// <summary>
    /// 自动化费用（为已选配方自动补种时）。原版扣费成功后会逐项触发 AutomationCostsConsumed（驱动“自动化花费”统计和数据触发器），
    /// 因此不能只把扣费换成校验，而是在 Prefix 中校验通过后直接返回成功、跳过原方法。
    /// </summary>
    [HarmonyPatch]
    private static class AutomationSelectedRecipe
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveFarmingPlot), "ConsumeSelectedRecipeAutomationCosts");

        private static bool Prefix(ActiveFarmingPlot __instance, ActiveFarmingRecipe recipe, ref bool __result) =>
            !TrySkipAutomationCosts(__instance, recipe, __instance.SelectedRecipe, ref __result);
    }

    /// <summary>自动化费用（为已种植的配方自动处理时）。逻辑同上，费用来源换成已种植的配方。</summary>
    [HarmonyPatch]
    private static class AutomationPlantedRecipe
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveFarmingPlot), "ConsumePlantedRecipeAutomationCosts");

        private static bool Prefix(ActiveFarmingPlot __instance, ActiveFarmingRecipe recipe, ref bool __result) =>
            !TrySkipAutomationCosts(__instance, recipe, __instance.PlantedRecipe, ref __result);
    }

    /// <summary>
    /// 磨坊。原版先校验原料，再 Execute 扣除，之后只是加产物和经验，没有“已消耗”类事件，
    /// 因此只把那一次 InventoryTransaction.Execute() 替换为 <see cref="InfiniteConsumables.ExecuteUnlessFree"/>。
    /// </summary>
    [HarmonyPatch]
    private static class GrainMill
    {
        private static readonly MethodInfo ExecuteUnlessFreeMethod =
            AccessTools.Method(typeof(InfiniteConsumables), nameof(InfiniteConsumables.ExecuteUnlessFree));

        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(FarmingGrainMillComponent), "GrainTimerFiredHandler");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            List<CodeInstruction> codes = instructions.ToList();
            int replaced = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!IsInventoryTransactionExecute(codes[i]))
                {
                    continue;
                }

                // 栈上已有 transaction：先压入类别，再把原调用换成 ExecuteUnlessFree(transaction, category)。
                // 若原调用上有跳转标签，移到新插入的第一条指令上，保证跳转后栈状态一致。
                var pushCategory = new CodeInstruction(OpCodes.Ldc_I4, (int)ConsumableCategory.GrainMill);
                pushCategory.labels.AddRange(codes[i].labels);
                codes[i].labels.Clear();
                codes[i].opcode = OpCodes.Call;
                codes[i].operand = ExecuteUnlessFreeMethod;
                codes.Insert(i, pushCategory);
                i++;
                replaced++;
            }

            if (replaced == 0)
            {
                ModLog.Warn($"{original.DeclaringType?.Name}.{original.Name} 中没有找到原料扣除，磨坊“不消耗”不会生效（游戏代码可能已变化）。");
            }

            return codes;
        }

        private static bool IsInventoryTransactionExecute(CodeInstruction instruction) =>
            (instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call)
            && instruction.operand is MethodInfo { Name: nameof(InventoryTransaction.Execute) } method
            && method.GetParameters().Length == 0
            && method.ReturnType == typeof(TransactionResult)
            && method.DeclaringType?.IsAssignableFrom(typeof(InventoryTransaction)) == true;
    }

    /// <summary>
    /// 牧场饲料与零食（Treats）。原版喂食处的保留判定会跳过整次喂食（饥饿值也不刷新），不适合强制，
    /// 因此改为在 TryConsumeFood 中只做原版的校验、不执行扣除：有饲料即视为喂食成功。
    /// </summary>
    [HarmonyPatch]
    private static class RanchingFeed
    {
        private static MethodBase TargetMethod() => PatchTarget.Declared(typeof(ActiveRanchingAnimal), "TryConsumeFood");

        private static bool Prefix(ActiveRanchingAnimal __instance, IReadOnlyList<ItemQuantity> items, ref bool __result)
        {
            if (!InfiniteConsumables.IsEnabled(ConsumableCategory.Ranching))
            {
                return true;
            }

            __result = __instance.Component.GetFoodConsumptionTransaction(items).Validate()
                == InventoryWalletTransactionResult.Success;
            return false;
        }
    }

    /// <summary>
    /// 开启时：自动化条件满足且费用足够 → 返回 true（表示已跳过原方法，结果为成功），不扣除、不触发“已花费”事件。
    /// 其余情况（关闭、条件不满足、没有配方、费用不足）返回 false，交给原方法处理，包括它自己的失败日志。
    /// </summary>
    private static bool TrySkipAutomationCosts(ActiveFarmingPlot plot, ActiveFarmingRecipe recipe, ActiveFarmingRecipe? costRecipe, ref bool result)
    {
        if (!InfiniteConsumables.IsEnabled(ConsumableCategory.FarmingAutomation)
            || recipe.AutomationRequirements is { AreMet: false }
            || costRecipe is null)
        {
            return false;
        }

        InventoryWalletTransaction costs = InfiniteConsumables.GetFarmingComponent(plot).GetRecipeAutomationCostsTransaction(costRecipe.Stats);
        if (costs.Validate() != InventoryWalletTransactionResult.Success)
        {
            return false;
        }

        result = true;
        return true;
    }
}
