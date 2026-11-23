using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.GameMenus;
using Godot;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.ItemSpawner;

/// <summary>可添加对象（物品或货币）的显示信息。</summary>
internal sealed record SpawnEntry<T>(T Object, string Name, string Id, Texture2D? Icon);

/// <summary>
/// 刷物品 / 货币。添加方式与官方调试菜单完全相同：
/// <c>Bank.Inventory.AddQuantity(item, n)</c> 与 <c>Bank.Wallet.AddQuantity(currency, n)</c>，
/// 因此物品获得事件、银行标签分配等都按游戏自身逻辑处理。
/// 只能在主线程、读档后使用。
/// </summary>
internal static class ItemSpawner
{
    // 弱引用：回到主菜单 / 换存档后，旧的 Game 不会被这里一直留在内存里
    private static readonly WeakReference<Game?> CachedGame = new(null);
    private static List<SpawnEntry<Item>> _items = [];
    private static List<SpawnEntry<Currency>> _currencies = [];

    /// <summary>当前存档的物品与货币目录（按名称排序）。换存档时重新生成。</summary>
    public static bool TryGetCatalog(
        out IReadOnlyList<SpawnEntry<Item>> items, out IReadOnlyList<SpawnEntry<Currency>> currencies)
    {
        items = [];
        currencies = [];
        if (!GameSession.TryGet(out GameView? _, out Game? game))
        {
            return false;
        }

        if (!CachedGame.TryGetTarget(out Game? cached) || !ReferenceEquals(cached, game))
        {
            _items = game.Data.GetDataRegistryOutput<Item>().GetAllObjects()
                .Select(i => new SpawnEntry<Item>(i, i.DisplayName.GetPluralText(1), i.Id, i.Icon))
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _currencies = game.Data.GetDataRegistryOutput<Currency>().GetAllObjects()
                .Select(c => new SpawnEntry<Currency>(c, c.DisplayName.Text, c.Id, c.Icon))
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            CachedGame.SetTarget(game);
            ModLog.Info($"刷物品：已加载 {_items.Count} 个物品、{_currencies.Count} 种货币。");
        }

        items = _items;
        currencies = _currencies;
        return true;
    }

    /// <summary>按名称或 ID 模糊筛选（不区分大小写，空格分隔的每个词都需匹配）。</summary>
    public static IEnumerable<SpawnEntry<T>> Filter<T>(IEnumerable<SpawnEntry<T>> entries, string query)
    {
        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length == 0
            ? entries
            : entries.Where(e => terms.All(t =>
                e.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase)
                || e.Id.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }

    public static int GetOwned(Item item) => TryGetBank(out BankEntity? bank) ? bank.Inventory.GetQuantity(item) : 0;

    public static long GetOwned(Currency currency) => TryGetBank(out BankEntity? bank) ? bank.Wallet.GetQuantity(currency) : 0;

    public static void AddItem(SpawnEntry<Item> entry, int quantity)
    {
        try
        {
            if (quantity <= 0 || !TryGetBank(out BankEntity? bank))
            {
                return;
            }

            int added = bank.Inventory.AddQuantity(entry.Object, quantity);
            ModLog.Info($"刷物品：{entry.Name}（{entry.Id}）+{added}");
        }
        catch (Exception ex)
        {
            ModLog.Error($"添加物品失败：{entry.Id}", ex);
        }
    }

    public static void AddCurrency(SpawnEntry<Currency> entry, long quantity)
    {
        try
        {
            if (quantity <= 0 || !TryGetBank(out BankEntity? bank))
            {
                return;
            }

            long added = bank.Wallet.AddQuantity(entry.Object, quantity);
            ModLog.Info($"刷货币：{entry.Name}（{entry.Id}）+{added}");
        }
        catch (Exception ex)
        {
            ModLog.Error($"添加货币失败：{entry.Id}", ex);
        }
    }

    private static bool TryGetBank([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BankEntity? bank)
    {
        bank = GameSession.TryGet(out GameView? _, out Game? game) ? GetBank(game) : null;
        return bank is not null;
    }

    /// <summary>Game.Bank 是私有属性。</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Bank")]
    private static extern BankEntity GetBank(Game game);
}
