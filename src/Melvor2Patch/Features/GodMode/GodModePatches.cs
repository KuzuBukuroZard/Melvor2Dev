using System.Reflection;
using GamesByMalcs.Melvor2.Components.Characters;
using HarmonyLib;

namespace Melvor2Patch.Features.GodMode;

/// <summary>无敌模式相关的 Harmony 补丁。</summary>
internal static class GodModePatches
{
    /// <summary>PlayerComponent 构造完成后登记它的 GlobalHealth（即全局唯一的玩家 HP）。</summary>
    [HarmonyPatch]
    private static class RegisterPlayerHealth
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredConstructors(typeof(PlayerComponent)).Where(c => !c.IsStatic);

        private static void Postfix(PlayerComponent __instance)
        {
            PlayerHealthRegistry.Register(__instance.GlobalHealth);
        }
    }

    /// <summary>
    /// 在玩家 HP 的扣血调用内部回滚 HP。
    /// <para>
    /// 原方法完整执行：扣除 min(伤害, 当前HP)、触发 Damaged、返回实际伤害值。
    /// Postfix 随后用公开 API <see cref="CharacterHealth.Set"/> 把 HP 恢复到受击前。
    /// 外层的 <see cref="TrackableCharacterHealth.Damage"/> 比较调用前后 HP 时看不到净变化，
    /// 因此不会触发 SelfCurrentChanged（死亡判定、自动进食都挂在它上面），
    /// 但仍会以真实伤害值触发 Damaged（伤害统计、任务目标、敌人吸血挂在它上面）。
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(CharacterHealth), nameof(CharacterHealth.Damage))]
    private static class RollbackPlayerDamage
    {
        private static void Prefix(CharacterHealth __instance, out int? __state)
        {
            __state = GodMode.Enabled && PlayerHealthRegistry.IsPlayerHealth(__instance)
                ? __instance.Current
                : null;
        }

        private static void Postfix(CharacterHealth __instance, int? __state)
        {
            if (__state is int before && __instance.Current != before)
            {
                __instance.Set(before);
            }
        }
    }
}
