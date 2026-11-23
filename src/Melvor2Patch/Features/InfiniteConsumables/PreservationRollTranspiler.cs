using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.InfiniteConsumables;

/// <summary>
/// 通用 Transpiler：在目标方法中找到“保留判定”的 <c>RollChance</c> 调用，并在其后插入改写结果的指令。
/// <para>
/// 判定规则：从 <c>RollChance</c> 往回找最近的一个名称以 <c>Chance</c> 结尾的属性 getter，
/// 只有它以 <c>PreservationChance</c> 结尾才算命中（如 <c>Stats.PreservationChance.Total.Value</c>、
/// <c>RuneStats.RunePreservationChance</c>）。概率先存入局部变量再传给 <c>RollChance(value)</c> 的写法同样适用。
/// 同一方法里的其他概率判定
/// （如狩猎的 <c>TrappingSuccessChance</c>、烹饪的 <c>PreservationChanceOnFail</c>、Artisan 的 <c>SkipStepChance</c>）不会被改动。
/// </para>
/// <para>
/// 插入的指令以栈顶的掷骰结果（bool）为输入、输出新的 bool，原版掷骰本身照常执行。
/// 一个都没命中时记录警告（通常意味着游戏更新改动了这段代码），方法保持原样。
/// </para>
/// </summary>
internal static class PreservationRollTranspiler
{
    private const int MaxLookBehind = 12;

    private static readonly MethodInfo ForcePreservationMethod =
        AccessTools.Method(typeof(InfiniteConsumables), nameof(InfiniteConsumables.ForcePreservation));

    private static readonly MethodInfo ForceSkillPreservationMethod =
        AccessTools.Method(typeof(InfiniteConsumables), nameof(InfiniteConsumables.ForceSkillPreservation));

    /// <summary>插入 <c>ForcePreservation(rolled, category)</c>。</summary>
    public static IEnumerable<CodeInstruction> ForCategory(
        IEnumerable<CodeInstruction> instructions, MethodBase original, ConsumableCategory category) =>
        Inject(instructions, original,
            new CodeInstruction(OpCodes.Ldc_I4, (int)category),
            new CodeInstruction(OpCodes.Call, ForcePreservationMethod));

    /// <summary>制造类技能：插入 <c>ForceSkillPreservation(rolled, this)</c>，由组件实例决定属于哪个技能。</summary>
    public static IEnumerable<CodeInstruction> ForSkillComponent(
        IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        Inject(instructions, original,
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, ForceSkillPreservationMethod));

    /// <summary>在每个命中的保留判定之后插入 <paramref name="injected"/> 的副本。</summary>
    public static IEnumerable<CodeInstruction> Inject(
        IEnumerable<CodeInstruction> instructions, MethodBase original, params CodeInstruction[] injected)
    {
        List<CodeInstruction> codes = instructions.ToList();
        int matched = 0;

        for (int i = 0; i < codes.Count; i++)
        {
            if (!IsRollChance(codes[i]) || !IsPreservationRoll(codes, i))
            {
                continue;
            }

            codes.InsertRange(i + 1, injected.Select(c => c.Clone()));
            i += injected.Length;
            matched++;
        }

        if (matched == 0)
        {
            ModLog.Warn($"{original.DeclaringType?.Name}.{original.Name} 中没有找到保留判定，该处“不消耗”不会生效（游戏代码可能已变化）。");
        }

        return codes;
    }

    private static bool IsRollChance(CodeInstruction instruction) =>
        (instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call)
        && instruction.operand is MethodInfo { Name: "RollChance" } method
        && method.ReturnType == typeof(bool);

    private static bool IsPreservationRoll(List<CodeInstruction> codes, int rollIndex)
    {
        for (int j = rollIndex - 1; j >= 0 && j >= rollIndex - MaxLookBehind; j--)
        {
            if (IsRollChance(codes[j]))
            {
                return false;
            }

            if (codes[j].operand is MethodInfo { Name: var name }
                && name.StartsWith("get_", StringComparison.Ordinal)
                && name.EndsWith("Chance", StringComparison.Ordinal))
            {
                return name.EndsWith("PreservationChance", StringComparison.Ordinal);
            }
        }

        return false;
    }
}
