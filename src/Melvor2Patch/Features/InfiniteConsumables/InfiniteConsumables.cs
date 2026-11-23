using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Components;
using GamesByMalcs.Melvor2.Components.ArtisanAction;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Entities.Beastcrafting;
using GamesByMalcs.Melvor2.Entities.Cooking;
using GamesByMalcs.Melvor2.Entities.Crafting;
using GamesByMalcs.Melvor2.Entities.Farming;
using GamesByMalcs.Melvor2.Entities.Firemaking;
using GamesByMalcs.Melvor2.Entities.Fletching;
using GamesByMalcs.Melvor2.Entities.Herblore;
using GamesByMalcs.Melvor2.Entities.Runecrafting;
using GamesByMalcs.Melvor2.Entities.Smithing;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.InfiniteConsumables;

/// <summary>可单独开关的消耗品类别。名称会写入 settings.json，改名时需在 <see cref="InfiniteConsumables.LegacyNames"/> 中保留旧名映射。</summary>
internal enum ConsumableCategory
{
    // 战斗
    Ammo,
    SpellRunes,
    ImbuementRunes,
    Prayer,
    Food,

    // 采集与敏捷
    Tools,
    Equipment,
    FishingBait,
    HuntingBait,
    Traps,
    AgilityEntry,

    // 制造类技能
    Cooking,
    Firemaking,
    Herblore,
    Smithing,
    Crafting,
    Fletching,
    Runecrafting,
    Beastcrafting,

    // 农牧
    Seeds,
    Compost,
    FarmingAutomation,
    GrainMill,
    Ranching,
}

/// <summary>UI 分组。</summary>
internal sealed record ConsumableGroup(string Title, IReadOnlyList<ConsumableCategory> Categories);

/// <summary>
/// 消耗品不消耗。
/// <para>
/// 主要手段是让游戏自己的“保留判定”（<c>RollChance(*PreservationChance)</c>）必定成功，由游戏走它自己的保留分支；
/// 消耗前的校验（库存是否足够等）保持原样。没有保留判定的消耗点改用 Prefix / Transpiler，
/// 只做原版校验、不执行扣除。各消耗点见 Patches 目录下按分组拆分的补丁文件。
/// </para>
/// </summary>
internal static class InfiniteConsumables
{
    private static readonly bool[] EnabledFlags = new bool[Enum.GetValues<ConsumableCategory>().Length];

    /// <summary>旧版配置中的类别名 → 拆分后的新类别（Bait / Runes 已按用途拆开）。</summary>
    private static readonly Dictionary<string, ConsumableCategory[]> LegacyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bait"] = [ConsumableCategory.FishingBait, ConsumableCategory.HuntingBait],
        ["Runes"] = [ConsumableCategory.SpellRunes, ConsumableCategory.ImbuementRunes],
    };

    public static IReadOnlyList<ConsumableCategory> Categories { get; } = Enum.GetValues<ConsumableCategory>();

    public static IReadOnlyList<ConsumableGroup> Groups { get; } =
    [
        new("Combat",
        [
            ConsumableCategory.Ammo, ConsumableCategory.SpellRunes, ConsumableCategory.ImbuementRunes,
            ConsumableCategory.Prayer, ConsumableCategory.Food,
        ]),
        new("Gathering & Agility",
        [
            ConsumableCategory.Tools, ConsumableCategory.Equipment, ConsumableCategory.FishingBait,
            ConsumableCategory.HuntingBait, ConsumableCategory.Traps, ConsumableCategory.AgilityEntry,
        ]),
        new("Artisan",
        [
            ConsumableCategory.Cooking, ConsumableCategory.Firemaking, ConsumableCategory.Herblore,
            ConsumableCategory.Smithing, ConsumableCategory.Crafting, ConsumableCategory.Fletching,
            ConsumableCategory.Runecrafting, ConsumableCategory.Beastcrafting,
        ]),
        new("Farming & Ranching",
        [
            ConsumableCategory.Seeds, ConsumableCategory.Compost, ConsumableCategory.FarmingAutomation,
            ConsumableCategory.GrainMill, ConsumableCategory.Ranching,
        ]),
    ];

    /// <summary>任一类别开关变化后触发（用于持久化）。</summary>
    public static event Action? Changed;

    public static bool IsEnabled(ConsumableCategory category) => Volatile.Read(ref EnabledFlags[(int)category]);

    public static void SetEnabled(ConsumableCategory category, bool enabled)
    {
        if (IsEnabled(category) == enabled)
        {
            return;
        }

        Volatile.Write(ref EnabledFlags[(int)category], enabled);
        ModLog.Info($"不消耗 - {GetDisplayName(category)}：{(enabled ? "开启" : "关闭")}");
        Changed?.Invoke();
    }

    /// <summary>解析配置文件中保存的类别名，兼容旧版名称。</summary>
    public static bool TryParseSavedName(string name, out IReadOnlyList<ConsumableCategory> categories)
    {
        if (LegacyNames.TryGetValue(name, out ConsumableCategory[]? mapped))
        {
            categories = mapped;
            return true;
        }

        // Enum.TryParse 也接受数字字符串，需额外确认是已定义的名称
        if (Enum.TryParse(name, ignoreCase: true, out ConsumableCategory category) && Enum.IsDefined(category))
        {
            categories = [category];
            return true;
        }

        categories = [];
        return false;
    }

    public static string GetDisplayName(ConsumableCategory category) => category switch
    {
        ConsumableCategory.SpellRunes => "Spell Runes",
        ConsumableCategory.ImbuementRunes => "Imbue Runes",
        ConsumableCategory.FishingBait => "Fishing Bait",
        ConsumableCategory.HuntingBait => "Hunting Bait",
        ConsumableCategory.AgilityEntry => "Agility Entry",
        ConsumableCategory.FarmingAutomation => "Automation",
        ConsumableCategory.GrainMill => "Grain Mill",
        ConsumableCategory.Ranching => "Ranching Feed",
        _ => category.ToString(),
    };

    /// <summary>UI 悬停提示。</summary>
    public static string GetDescription(ConsumableCategory category) => category switch
    {
        ConsumableCategory.Ammo => "Ammo is not used up when attacking.",
        ConsumableCategory.SpellRunes => "Rune costs of combat spells are not deducted.\nYou still need enough runes to cast.",
        ConsumableCategory.ImbuementRunes => "Rune costs of imbuements are not deducted.\nYou still need enough runes for the imbuement to stay active.",
        ConsumableCategory.Prayer => "Prayer point costs are not deducted.\nYou still need enough points to activate a prayer.",
        ConsumableCategory.Food => "Eating (manual or auto) still heals, but food is not used up.",
        ConsumableCategory.Tools => "Toolbelt items are not used up when they trigger.",
        ConsumableCategory.Equipment => "Equipped items that are consumed when they trigger are not used up.",
        ConsumableCategory.FishingBait => "Fishing bait is not used up.",
        ConsumableCategory.HuntingBait => "Hunting bait is not used up.\nYou still need the bait in your bank for it to take effect.",
        ConsumableCategory.Traps => "Hunting traps are not used up.",
        ConsumableCategory.AgilityEntry => "Agility course entry costs are not deducted.\nYou still need to be able to afford them.",
        ConsumableCategory.Cooking or ConsumableCategory.Firemaking or ConsumableCategory.Herblore
            or ConsumableCategory.Smithing or ConsumableCategory.Crafting or ConsumableCategory.Fletching
            or ConsumableCategory.Runecrafting or ConsumableCategory.Beastcrafting =>
            $"{GetDisplayName(category)} ingredients are not used up.\nYou still need the ingredients to start and keep crafting.",
        ConsumableCategory.Seeds => "Seeds are not used up when planting.\nYou still need the seeds in your bank.",
        ConsumableCategory.Compost => "Compost is not used up.\nYou still need the selected compost in your bank.",
        ConsumableCategory.FarmingAutomation => "Farming automation costs are not deducted.\nYou still need to be able to afford them.",
        ConsumableCategory.GrainMill => "The grain mill does not use up its input items.\nYou still need them in your bank.",
        ConsumableCategory.Ranching => "Animals are fed without using up feed or treats.\nYou still need them in your bank.",
        _ => string.Empty,
    };

    /// <summary>由 Transpiler 注入到保留判定之后：<c>rolled = ForcePreservation(rolled, category)</c>。</summary>
    public static bool ForcePreservation(bool rolled, ConsumableCategory category) => rolled || IsEnabled(category);

    /// <summary>
    /// 制造类技能专用，由 Transpiler 注入：<c>rolled = ForceSkillPreservation(rolled, this)</c>。
    /// Artisan 的 5 个技能共用同一个方法、Firemaking 使用泛型基类的方法，因此按组件实例判断所属技能。
    /// </summary>
    public static bool ForceSkillPreservation(bool rolled, GameComponent component) =>
        rolled || (GetSkillCategory(component) is { } category && IsEnabled(category));

    /// <summary>
    /// 堆肥专用：原版的堆肥保留分支不检查库存（<c>CanCompost</c> 也不检查），
    /// 因此只有库存里确实有所选堆肥时才强制保留，避免“没有堆肥也能施肥”。
    /// </summary>
    public static bool ForceCompostPreservation(bool rolled, ActiveFarmingPlot plot)
    {
        if (rolled || !IsEnabled(ConsumableCategory.Compost))
        {
            return rolled;
        }

        return plot.SelectedCompost is { } compost && GetFarmingComponent(plot).Inventory.GetQuantity(compost) > 0;
    }

    /// <summary>
    /// 磨坊专用，由 Transpiler 替换原来的 <c>InventoryTransaction.Execute()</c> 调用。
    /// 原版在调用前已经校验过库存，开启时直接视为成功、不扣除。
    /// </summary>
    public static TransactionResult ExecuteUnlessFree(InventoryTransaction transaction, ConsumableCategory category) =>
        IsEnabled(category) ? TransactionResult.Success : transaction.Execute();

    /// <summary>ActiveFarmingPlot.Component 是私有属性。</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Component")]
    public static extern FarmingPassiveComponent GetFarmingComponent(ActiveFarmingPlot plot);

    private static ConsumableCategory? GetSkillCategory(GameComponent component) => component switch
    {
        CookingActionComponent => ConsumableCategory.Cooking,
        HerbloreActionComponent => ConsumableCategory.Herblore,
        FiremakingActionComponent => ConsumableCategory.Firemaking,
        ArtisanActionComponent => component.Entity switch
        {
            SmithingEntity => ConsumableCategory.Smithing,
            CraftingEntity => ConsumableCategory.Crafting,
            FletchingEntity => ConsumableCategory.Fletching,
            RunecraftingEntity => ConsumableCategory.Runecrafting,
            BeastcraftingEntity => ConsumableCategory.Beastcrafting,
            _ => null,
        },
        _ => null,
    };
}
