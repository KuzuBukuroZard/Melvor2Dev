using Godot;
using Melvor2Patch.Core;
using Melvor2Patch.Features;
using Melvor2Patch.Features.GodMode;
using Melvor2Patch.Features.InfiniteConsumables;
using Melvor2Patch.Features.OfficialDebugMenu;
using Melvor2Patch.Features.TimeMachine;

namespace Melvor2Patch.UI;

/// <summary>
/// 游戏内菜单：一个可拖动、可折叠的小窗口（见 <see cref="ModWindow"/>），按分组排列各功能。
/// 只能在游戏就绪阶段（<see cref="Entry.OnMainReady"/>）之后创建。尺寸均为 4px 的倍数。
/// </summary>
internal static class ModMenu
{
    private const string NodeName = "Melvor2PatchMenu";
    private const string StateFileName = "ui-state.json";
    private const string RequiresSave = "\n(Requires a loaded save.)";

    private static readonly Color SectionHeaderColor = new(1f, 1f, 1f, 0.55f);
    private static readonly Color GroupHeaderColor = new(1f, 1f, 1f, 0.8f);

    /// <summary>只有读档后才能用的按钮；未读档时禁用。</summary>
    private static readonly List<BaseButton> SessionButtons = [];

    private static SpawnerWindow? _spawner;
    private static bool? _inGame;

    public static void Attach(Node host)
    {
        if (host.HasNode(NodeName))
        {
            return;
        }

        var window = new ModWindow($"{ModContext.ModName} v{ModContext.Version}", ModContext.GetPath(StateFileName));
        _spawner = new SpawnerWindow();
        List<CheckBox> consumableBoxes = BuildContent(window.Content);

        // 各分组是独立的网格，列宽各自计算；进入场景树后（主题字体可用时）统一勾选框宽度，让各组的列对齐
        window.Root.Ready += () => EqualizeWidths(consumableBoxes);

        var layer = new CanvasLayer
        {
            Name = NodeName,
            Layer = 128,
        };
        layer.AddChild(window.Root);
        layer.AddChild(_spawner.Root);

        // 会话在读档的后台线程中开始，这里定期在主线程上查询，避免跨线程操作控件
        var sessionTimer = new Godot.Timer { WaitTime = 0.5, Autostart = true };
        sessionTimer.Timeout += RefreshSessionState;
        layer.AddChild(sessionTimer);

        // ModWindow 实例由它连接的信号委托保持存活，无需另外持有
        RefreshSessionState();

        // 宿主可能正处于 _Ready 流程中，延迟到帧末再挂入场景树
        host.CallDeferred(Node.MethodName.AddChild, layer);
        ModLog.Info("游戏内菜单已添加。");
    }

    /// <summary>按“常用操作在上、长列表在下”的顺序排列各区块（Cheats → Time Skip → Tools → No Consumption），返回消耗品勾选框（用于对齐宽度）。</summary>
    private static List<CheckBox> BuildContent(VBoxContainer content)
    {
        content.AddChild(CreateSectionHeader("Cheats"));
        content.AddChild(CreateCheckBox(
            "God Mode",
            "Damage is still dealt and counted in statistics,\nbut your Hitpoints are restored immediately, so you cannot die.",
            GodMode.Enabled,
            value => GodMode.Enabled = value));

        content.AddChild(new HSeparator());
        AddTimeSkipSection(content);

        content.AddChild(new HSeparator());
        content.AddChild(CreateSectionHeader("Tools"));
        Button spawner = CreateButton(
            "Item Spawner",
            "Search for items and add them to your bank, or add currencies." + RequiresSave,
            () => _spawner?.Toggle());
        SessionButtons.Add(spawner);
        content.AddChild(spawner);

        Button debugMenu = CreateButton(
            "Open Official Debug Menu",
            "Opens the developer debug menu that is left in the game.\nIts actions are applied directly to your save." + RequiresSave,
            OfficialDebugMenu.Open);
        SessionButtons.Add(debugMenu);
        content.AddChild(debugMenu);

        content.AddChild(new HSeparator());
        return AddConsumablesSection(content);
    }

    private static void AddTimeSkipSection(VBoxContainer content)
    {
        content.AddChild(CreateSectionHeader("Time Skip"));

        var hours = new SpinBox
        {
            MinValue = TimeMachine.MinHours,
            MaxValue = TimeMachine.MaxHours,
            Step = 0.5,
            Value = FeatureSettings.TimeSkipHours,
            Suffix = "h",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = $"Hours to skip ({TimeMachine.MinHours:0.#} - {TimeMachine.MaxHours:0} h).",
        };
        hours.ValueChanged += value => FeatureSettings.TimeSkipHours = value;

        Button skip = CreateButton(
            "Skip",
            "Fast-forwards the selected time using the game's own offline progress." + RequiresSave,
            () => TimeMachine.Skip(hours.Value));
        skip.CustomMinimumSize = new Vector2(64f, 0f);
        SessionButtons.Add(skip);

        var inputRow = new HBoxContainer();
        inputRow.AddThemeConstantOverride("separation", 8);
        inputRow.AddChild(hours);
        inputRow.AddChild(skip);
        content.AddChild(inputRow);

        // 预设只填入时长，仍需点击 Skip，避免误触直接跳过大段时间
        var presetRow = new HBoxContainer();
        presetRow.AddThemeConstantOverride("separation", 4);
        foreach (double preset in new[] { 1.0, 4.0, 8.0, 12.0, 24.0 })
        {
            Button button = CreateButton($"{preset:0}h", $"Set to {preset:0} hours", () => hours.Value = preset);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.AddThemeFontSizeOverride("font_size", 12);
            presetRow.AddChild(button);
        }

        content.AddChild(presetRow);
    }

    /// <summary>
    /// 消耗品：区块标题的 All/None 作用于全部类别；每个分组有自己的小标题和 All/None，下面是两列勾选框。
    /// 设置 ButtonPressed 会触发 Toggled，开关状态与配置随之更新。
    /// </summary>
    private static List<CheckBox> AddConsumablesSection(VBoxContainer content)
    {
        var allBoxes = new List<CheckBox>();
        var groups = new VBoxContainer();
        groups.AddThemeConstantOverride("separation", 8);

        foreach (ConsumableGroup group in InfiniteConsumables.Groups)
        {
            var boxes = new List<CheckBox>();
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 8);
            grid.AddThemeConstantOverride("v_separation", 4);

            foreach (ConsumableCategory category in group.Categories)
            {
                CheckBox box = CreateCheckBox(
                    InfiniteConsumables.GetDisplayName(category),
                    InfiniteConsumables.GetDescription(category),
                    InfiniteConsumables.IsEnabled(category),
                    value => InfiniteConsumables.SetEnabled(category, value));
                boxes.Add(box);
                grid.AddChild(box);
            }

            allBoxes.AddRange(boxes);

            var groupBox = new VBoxContainer();
            groupBox.AddThemeConstantOverride("separation", 4);
            groupBox.AddChild(CreateGroupHeader(
                group.Title,
                CreateLinkButton("All", $"Enable all in {group.Title}", () => SetAll(boxes, true)),
                CreateLinkButton("None", $"Disable all in {group.Title}", () => SetAll(boxes, false))));
            groupBox.AddChild(grid);
            groups.AddChild(groupBox);
        }

        content.AddChild(CreateSectionHeader(
            "No Consumption",
            CreateLinkButton("All", "Enable every category", () => SetAll(allBoxes, true)),
            CreateLinkButton("None", "Disable every category", () => SetAll(allBoxes, false))));
        content.AddChild(groups);
        return allBoxes;
    }

    private static void RefreshSessionState()
    {
        bool inGame = GameSession.TryGet(out _, out _);
        if (_inGame == inGame)
        {
            return;
        }

        _inGame = inGame;
        foreach (BaseButton button in SessionButtons)
        {
            button.Disabled = !inGame;
        }

        // 离开存档时关闭刷物品窗口，避免对已释放的存档操作
        if (!inGame)
        {
            _spawner?.Close();
        }
    }

    private static void SetAll(IEnumerable<CheckBox> boxes, bool pressed)
    {
        foreach (CheckBox box in boxes)
        {
            box.ButtonPressed = pressed;
        }
    }

    /// <summary>把一组控件的最小宽度统一为其中最宽者（向上取整到 4 的倍数）。</summary>
    private static void EqualizeWidths(IReadOnlyList<Control> controls)
    {
        if (controls.Count == 0)
        {
            return;
        }

        float width = MathF.Ceiling(controls.Max(c => c.GetCombinedMinimumSize().X) / 4f) * 4f;
        foreach (Control control in controls)
        {
            control.CustomMinimumSize = new Vector2(width, control.CustomMinimumSize.Y);
        }
    }

    // ───────────── 控件工厂 ─────────────

    /// <summary>区块标题：小号、半透明的大写文字，右侧可附加按钮。</summary>
    private static HBoxContainer CreateSectionHeader(string text, params Control[] trailing) =>
        CreateHeader(text.ToUpperInvariant(), SectionHeaderColor, trailing);

    /// <summary>分组小标题：比区块标题更亮、不转大写，用于区块内部的分组。</summary>
    private static HBoxContainer CreateGroupHeader(string text, params Control[] trailing) =>
        CreateHeader(text, GroupHeaderColor, trailing);

    private static HBoxContainer CreateHeader(string text, Color color, Control[] trailing)
    {
        var row = new HBoxContainer();
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", color);

        row.AddChild(label);
        foreach (Control control in trailing)
        {
            row.AddChild(control);
        }

        return row;
    }

    private static CheckBox CreateCheckBox(string text, string tooltip, bool initial, Action<bool> setter)
    {
        var box = new CheckBox
        {
            Text = text,
            TooltipText = tooltip,
            ButtonPressed = initial,
            FocusMode = Control.FocusModeEnum.None,
        };
        box.Toggled += pressed => setter(pressed);
        return box;
    }

    private static Button CreateButton(string text, string tooltip, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.Pressed += onPressed;
        return button;
    }

    /// <summary>无背景的小号文字按钮，用于标题右侧。</summary>
    private static Button CreateLinkButton(string text, string tooltip, Action onPressed)
    {
        Button button = CreateButton(text, tooltip, onPressed);
        button.Flat = true;
        button.AddThemeFontSizeOverride("font_size", 12);
        return button;
    }
}
