using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using Godot;
using Melvor2Patch.Core;
using Melvor2Patch.Features.ItemSpawner;

namespace Melvor2Patch.UI;

/// <summary>
/// 刷物品 / 货币窗口：独立的可拖动、可关闭小窗口（见 <see cref="ModWindow"/>），从主菜单打开。
/// <list type="bullet">
/// <item>物品：按名称或 ID 搜索（空格分隔多个关键词），单击选中、双击或在搜索框按回车添加。</item>
/// <item>货币：下拉选择后添加。</item>
/// </list>
/// 添加逻辑见 <see cref="ItemSpawner"/>。尺寸均为 4px 的倍数。
/// </summary>
internal sealed class SpawnerWindow
{
    private const string StateFileName = "spawner-state.json";
    private const int IconSize = 24;

    private static readonly Color MutedColor = new(1f, 1f, 1f, 0.55f);

    private readonly ModWindow _window;
    private readonly LineEdit _search;
    private readonly ItemList _itemList;
    private readonly Label _itemCount;
    private readonly Label _itemOwned;
    private readonly SpinBox _itemQuantity;
    private readonly OptionButton _currencyOptions;
    private readonly Label _currencyOwned;
    private readonly SpinBox _currencyQuantity;

    /// <summary>当前列表中显示的物品（与 ItemList 的索引一一对应）。</summary>
    private readonly List<SpawnEntry<Item>> _shownItems = [];

    private IReadOnlyList<SpawnEntry<Item>> _items = [];
    private IReadOnlyList<SpawnEntry<Currency>> _currencies = [];

    public SpawnerWindow()
    {
        _window = new ModWindow("Item Spawner", ModContext.GetPath(StateFileName), closable: true, defaultAnchor: new Vector2(0.5f, 0.5f));
        _window.Root.Visible = false;
        VBoxContainer content = _window.Content;

        // ───── 物品 ─────
        content.AddChild(CreateHeader("ITEMS"));

        _search = new LineEdit
        {
            PlaceholderText = "Search by name or ID",
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(360f, 0f),
        };
        _search.TextChanged += _ => RefillItems();
        _search.TextSubmitted += _ => AddSelectedItem();
        content.AddChild(_search);

        _itemList = new ItemList
        {
            SelectMode = ItemList.SelectModeEnum.Single,
            FixedIconSize = new Vector2I(IconSize, IconSize),
            CustomMinimumSize = new Vector2(360f, 320f),
            FocusMode = Control.FocusModeEnum.Click,
        };
        _itemList.ItemSelected += _ => UpdateItemOwned();
        _itemList.ItemActivated += _ => AddSelectedItem();
        content.AddChild(_itemList);

        _itemCount = CreateMutedLabel();
        _itemOwned = CreateMutedLabel();
        _itemOwned.HorizontalAlignment = HorizontalAlignment.Right;
        _itemOwned.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var infoRow = new HBoxContainer();
        infoRow.AddChild(_itemCount);
        infoRow.AddChild(_itemOwned);
        content.AddChild(infoRow);

        _itemQuantity = CreateQuantity(int.MaxValue, "Quantity to add");
        content.AddChild(CreateAddRow(_itemQuantity, "Add the selected item to your bank.\nDouble-click an item to add it directly.", AddSelectedItem));
        content.AddChild(CreatePresetRow(_itemQuantity, [1, 10, 100, 1000, 10000]));

        content.AddChild(new HSeparator());

        // ───── 货币 ─────
        content.AddChild(CreateHeader("CURRENCIES"));

        _currencyOptions = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _currencyOptions.AddThemeConstantOverride("icon_max_width", IconSize);
        _currencyOptions.GetPopup().AddThemeConstantOverride("icon_max_width", IconSize);
        _currencyOptions.ItemSelected += _ => UpdateCurrencyOwned();

        _currencyOwned = CreateMutedLabel();
        var currencyRow = new HBoxContainer();
        currencyRow.AddThemeConstantOverride("separation", 8);
        currencyRow.AddChild(_currencyOptions);
        currencyRow.AddChild(_currencyOwned);
        content.AddChild(currencyRow);

        // SpinBox 内部是 double，1e15 以内可以精确表示整数
        _currencyQuantity = CreateQuantity(1e15, "Amount to add");
        content.AddChild(CreateAddRow(_currencyQuantity, "Add the selected currency.", AddSelectedCurrency));
        content.AddChild(CreatePresetRow(_currencyQuantity, [1000, 100000, 1000000, 100000000]));
    }

    /// <summary>窗口根节点，加入 CanvasLayer 即可。</summary>
    public Control Root => _window.Root;

    public bool IsOpen => _window.Root.Visible;

    /// <summary>打开窗口；首次打开或换了存档时重新加载物品与货币目录。</summary>
    public void Open()
    {
        if (!ItemSpawner.TryGetCatalog(out IReadOnlyList<SpawnEntry<Item>> items, out IReadOnlyList<SpawnEntry<Currency>> currencies))
        {
            ModLog.Warn("刷物品只能在游戏内（读档后）使用。");
            return;
        }

        if (!ReferenceEquals(items, _items))
        {
            _items = items;
            RefillItems();
        }

        if (!ReferenceEquals(currencies, _currencies))
        {
            _currencies = currencies;
            RefillCurrencies();
        }

        UpdateItemOwned();
        UpdateCurrencyOwned();
        _window.Show();
        _search.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => _window.Hide();

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void RefillItems()
    {
        _itemList.Clear();
        _shownItems.Clear();
        _shownItems.AddRange(ItemSpawner.Filter(_items, _search.Text));

        foreach (SpawnEntry<Item> entry in _shownItems)
        {
            int index = _itemList.AddItem(entry.Name, entry.Icon);
            _itemList.SetItemTooltip(index, entry.Id);
        }

        _itemCount.Text = $"{_shownItems.Count} / {_items.Count} items";

        // 搜索结果唯一时自动选中，回车/点 Add 即可添加
        if (_shownItems.Count == 1)
        {
            _itemList.Select(0);
        }

        UpdateItemOwned();
    }

    private void RefillCurrencies()
    {
        _currencyOptions.Clear();
        for (int i = 0; i < _currencies.Count; i++)
        {
            SpawnEntry<Currency> entry = _currencies[i];
            if (entry.Icon is not null)
            {
                _currencyOptions.AddIconItem(entry.Icon, entry.Name, i);
            }
            else
            {
                _currencyOptions.AddItem(entry.Name, i);
            }
        }

        if (_currencies.Count > 0)
        {
            _currencyOptions.Select(0);
        }
    }

    private SpawnEntry<Item>? SelectedItem()
    {
        int[] selected = _itemList.GetSelectedItems();
        return selected.Length > 0 && selected[0] < _shownItems.Count ? _shownItems[selected[0]] : null;
    }

    private SpawnEntry<Currency>? SelectedCurrency()
    {
        int index = _currencyOptions.Selected;
        return index >= 0 && index < _currencies.Count ? _currencies[index] : null;
    }

    private void AddSelectedItem()
    {
        if (SelectedItem() is { } entry)
        {
            ItemSpawner.AddItem(entry, (int)_itemQuantity.Value);
            UpdateItemOwned();
        }
    }

    private void AddSelectedCurrency()
    {
        if (SelectedCurrency() is { } entry)
        {
            ItemSpawner.AddCurrency(entry, (long)_currencyQuantity.Value);
            UpdateCurrencyOwned();
        }
    }

    private void UpdateItemOwned() =>
        _itemOwned.Text = SelectedItem() is { } entry ? $"Owned: {ItemSpawner.GetOwned(entry.Object):N0}" : string.Empty;

    private void UpdateCurrencyOwned() =>
        _currencyOwned.Text = SelectedCurrency() is { } entry ? $"Owned: {ItemSpawner.GetOwned(entry.Object):N0}" : string.Empty;

    // ───────────── 控件工厂 ─────────────

    private static Label CreateHeader(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", MutedColor);
        return label;
    }

    private static Label CreateMutedLabel()
    {
        var label = new Label { VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", MutedColor);
        return label;
    }

    private static SpinBox CreateQuantity(double max, string tooltip) => new()
    {
        MinValue = 1,
        MaxValue = max,
        Step = 1,
        Value = 1,
        Rounded = true,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        TooltipText = tooltip,
    };

    private static HBoxContainer CreateAddRow(SpinBox quantity, string tooltip, Action onAdd)
    {
        var add = new Button
        {
            Text = "Add",
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(64f, 0f),
        };
        add.Pressed += onAdd;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(quantity);
        row.AddChild(add);
        return row;
    }

    /// <summary>数量预设：只填入数值，仍需点击 Add。</summary>
    private static HBoxContainer CreatePresetRow(SpinBox quantity, double[] presets)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        foreach (double preset in presets)
        {
            var button = new Button
            {
                Text = FormatShort(preset),
                TooltipText = $"Set to {preset:N0}",
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            button.AddThemeFontSizeOverride("font_size", 12);
            button.Pressed += () => quantity.Value = preset;
            row.AddChild(button);
        }

        return row;
    }

    private static string FormatShort(double value) => value switch
    {
        >= 1e9 => $"{value / 1e9:0.#}B",
        >= 1e6 => $"{value / 1e6:0.#}M",
        >= 1e3 => $"{value / 1e3:0.#}K",
        _ => $"{value:0}",
    };
}
