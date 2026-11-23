using System.Reflection;
using GamesByMalcs.Melvor2.Components.ArtisanAction;
using GamesByMalcs.Melvor2.Components.CraftingAction;
using GamesByMalcs.Melvor2.Entities.Cooking;
using GamesByMalcs.Melvor2.Entities.Firemaking;
using GamesByMalcs.Melvor2.Entities.Herblore;
using HarmonyLib;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.InfiniteConsumables.Patches;

/// <summary>
/// 制造类技能的原料消耗。所有制造技能的动作处理都是同一套流程：
/// 校验原料 → 保留判定 → 判定失败才扣原料，成功则走官方的保留分支（触发“物品被保留”事件），产物与经验照常。
/// 注入的判定按组件实例区分技能，见 <see cref="InfiniteConsumables.ForceSkillPreservation"/>。
/// </summary>
internal static class ArtisanConsumablePatches
{
    /// <summary>
    /// 重写了动作处理的技能：Cooking、Herblore，以及 Artisan（Smithing / Crafting / Fletching / Runecrafting / Beastcrafting 共用）。
    /// Artisan 每一步都会校验原料，但只在物品完成时才扣除。
    /// </summary>
    [HarmonyPatch]
    private static class CraftingSkills
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
        [
            PatchTarget.Declared(typeof(CookingActionComponent), "ActionTimerFiredHandler"),
            PatchTarget.Declared(typeof(HerbloreActionComponent), "ActionTimerFiredHandler"),
            PatchTarget.Declared(typeof(ArtisanActionComponent), "ActionTimerFiredHandler"),
        ];

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForSkillComponent(instructions, original);
    }

    /// <summary>
    /// Firemaking 没有重写动作处理，使用的是泛型基类 CraftingActionComponent&lt;TRecipe, TActiveRecipe&gt; 的默认实现。
    /// 类型参数都是引用类型，运行时所有实例化共享同一份代码，所以补丁会作用于每个调用这个基类方法的技能
    /// （目前只有 Firemaking，其余技能都重写了且不调用基类），注入的判定按实例区分技能，不会误伤。
    /// 单独成类：泛型补丁万一失败，不影响上面的其他制造技能。
    /// </summary>
    [HarmonyPatch]
    private static class Firemaking
    {
        private static MethodBase TargetMethod() =>
            PatchTarget.Declared(typeof(CraftingActionComponent<FiremakingRecipe, ActiveFiremakingRecipe>), "ActionTimerFiredHandler");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            PreservationRollTranspiler.ForSkillComponent(instructions, original);
    }
}
