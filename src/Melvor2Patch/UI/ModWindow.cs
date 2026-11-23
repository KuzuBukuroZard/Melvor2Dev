using Godot;

namespace Melvor2Patch.UI;

/// <summary>
/// 可拖动、可折叠的小窗口外壳，完全由 GodotSharp 原生控件构成。
/// <list type="bullet">
/// <item>拖动标题栏移动窗口，双击标题栏或点击右侧按钮折叠/展开；折叠后只剩标题栏，作为常驻入口。</item>
/// <item>窗口始终限制在屏幕内（拖动、展开、分辨率变化时）；内容高于屏幕时内容区出现滚动条。</item>
/// <item>位置与折叠状态保存在 <see cref="WindowState"/> 中，下次启动恢复。</item>
/// <item>窗口自带一个 Theme，只定义悬停提示的样式：游戏的项目主题里提示框没有背景，我们的控件不在游戏 UI 树下，会落到项目主题上。</item>
/// </list>
/// <para>
/// 这里不继承任何 Godot 类型：mod 程序集没有 Godot 源生成器，自定义子类重写的虚方法（_GuiInput 等）
/// 不会被引擎调用，所以所有交互都通过信号（C# 事件）实现。
/// </para>
/// 尺寸均为 4px 的倍数。
/// </summary>
internal sealed class ModWindow
{
    private const int CornerRadius = 3;
    private const float ScreenMargin = 8f;
    private const float MinBodyHeight = 120f;

    private static readonly Color TextColor = new(0.93f, 0.93f, 0.93f);

    private readonly WindowState _state;
    private readonly string _statePath;
    private readonly Vector2 _defaultAnchor;
    private readonly StyleBoxFlat _titleStyle;
    private readonly PanelContainer _titleBar;
    private readonly Button _collapseButton;
    private readonly ScrollContainer _body;
    private readonly MarginContainer _bodyContent;

    private bool _dragging;
    private Vector2 _dragOffset;
    private bool _placed;

    /// <param name="title">标题栏文字。</param>
    /// <param name="statePath">位置与折叠状态的保存路径。</param>
    /// <param name="closable">是否显示关闭按钮（关闭即隐藏，可通过 <see cref="Show"/> 重新打开）。</param>
    /// <param name="defaultAnchor">没有保存位置时的初始位置，按屏幕可用空间的比例（0~1）。默认屏幕左侧中部。</param>
    public ModWindow(string title, string statePath, bool closable = false, Vector2? defaultAnchor = null)
    {
        _statePath = statePath;
        _state = WindowState.Load(statePath);
        _defaultAnchor = defaultAnchor ?? new Vector2(0f, 0.5f);

        Root = new PanelContainer
        {
            Name = "Window",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Theme = CreateTheme(),
        };
        Root.AddThemeStyleboxOverride("panel", CreateWindowStyle());

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 0);

        // 标题栏：拖动 / 双击折叠
        _titleStyle = CreateTitleStyle();
        _titleBar = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Move,
            TooltipText = "Drag to move. Double-click to collapse or expand.",
        };
        _titleBar.AddThemeStyleboxOverride("panel", _titleStyle);
        _titleBar.GuiInput += OnTitleBarInput;

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 8);

        var titleLabel = new Label
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 16);

        _collapseButton = new Button
        {
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(28f, 0f),
        };
        _collapseButton.Pressed += () => SetCollapsed(!_state.Collapsed);

        titleRow.AddChild(titleLabel);
        titleRow.AddChild(_collapseButton);
        if (closable)
        {
            var closeButton = new Button
            {
                Text = "×",
                Flat = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(28f, 0f),
                TooltipText = "Close",
            };
            closeButton.Pressed += Hide;
            titleRow.AddChild(closeButton);
        }

        _titleBar.AddChild(titleRow);

        // 内容区：横向不滚动（宽度跟随内容），纵向在超出屏幕时滚动，高度由 FitBodyToViewport 计算
        _body = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };

        _bodyContent = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bodyContent.AddThemeConstantOverride("margin_left", 12);
        _bodyContent.AddThemeConstantOverride("margin_right", 12);
        _bodyContent.AddThemeConstantOverride("margin_top", 8);
        _bodyContent.AddThemeConstantOverride("margin_bottom", 12);

        Content = new VBoxContainer();
        Content.AddThemeConstantOverride("separation", 8);
        _bodyContent.AddChild(Content);
        _body.AddChild(_bodyContent);

        layout.AddChild(_titleBar);
        layout.AddChild(_body);
        Root.AddChild(layout);

        Root.Ready += OnReady;
        ApplyCollapsed();
    }

    /// <summary>窗口根节点，加入任意 CanvasLayer 即可显示。</summary>
    public PanelContainer Root { get; }

    /// <summary>窗口内容容器。</summary>
    public VBoxContainer Content { get; }

    /// <summary>显示窗口（展开并置于同层最前）。</summary>
    public void Show()
    {
        Root.Visible = true;
        Root.MoveToFront();
        if (_state.Collapsed)
        {
            SetCollapsed(false);
        }
        else
        {
            QueueRelayout();
        }
    }

    public void Hide()
    {
        _dragging = false;
        Root.Visible = false;
    }

    /// <summary>内容大小变化后调用，重新计算窗口尺寸与位置。</summary>
    public void QueueRelayout() => Callable.From(Relayout).CallDeferred();

    private void OnReady()
    {
        Root.GetViewport().SizeChanged += QueueRelayout;
        QueueRelayout();
    }

    private void OnTitleBarInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed && button.DoubleClick)
                {
                    _dragging = false;
                    SetCollapsed(!_state.Collapsed);
                }
                else if (button.Pressed)
                {
                    _dragging = true;
                    _dragOffset = Root.GetGlobalMousePosition() - Root.Position;
                }
                else if (_dragging)
                {
                    _dragging = false;
                    SaveState();
                }

                _titleBar.AcceptEvent();
                break;

            // 按下后 Godot 会把后续鼠标事件持续发给这个控件，即使鼠标移出了标题栏
            case InputEventMouseMotion when _dragging:
                Root.Position = ClampToViewport(Root.GetGlobalMousePosition() - _dragOffset);
                _titleBar.AcceptEvent();
                break;
        }
    }

    private void SetCollapsed(bool collapsed)
    {
        _state.Collapsed = collapsed;
        ApplyCollapsed();
        QueueRelayout();
        SaveState();
    }

    private void ApplyCollapsed()
    {
        bool collapsed = _state.Collapsed;
        _body.Visible = !collapsed;
        _collapseButton.Text = collapsed ? "+" : "-";
        _collapseButton.TooltipText = collapsed ? "Expand" : "Collapse";

        // 折叠时标题栏就是整个窗口，下方两个角也要圆角
        int bottomRadius = collapsed ? CornerRadius - 1 : 0;
        _titleStyle.CornerRadiusBottomLeft = bottomRadius;
        _titleStyle.CornerRadiusBottomRight = bottomRadius;
    }

    /// <summary>
    /// 内容变化后，最小尺寸要等到帧末才更新；且脱离容器的 Control 不会自动缩小，
    /// 因此延迟到帧末（见 <see cref="QueueRelayout"/>）再计算内容区高度、收缩到最小尺寸并重新限制位置。
    /// </summary>
    private void Relayout()
    {
        if (!GodotObject.IsInstanceValid(Root) || !Root.IsInsideTree())
        {
            return;
        }

        if (!_state.Collapsed)
        {
            FitBodyToViewport();
        }

        Root.ResetSize();

        if (!_placed)
        {
            _placed = true;
            Vector2 free = Root.GetViewportRect().Size - Root.Size;
            Root.Position = _state.X is float x && _state.Y is float y
                ? new Vector2(x, y)
                : new Vector2(Math.Max(16f, free.X * _defaultAnchor.X), free.Y * _defaultAnchor.Y);
        }

        Root.Position = ClampToViewport(Root.Position);
    }

    /// <summary>
    /// 内容区高度 = min(内容实际高度, 屏幕可用高度)。纵向滚动的 ScrollContainer 不会把内容高度算进自身的最小尺寸，
    /// 所以这里手动设置；出现滚动条时再加上滚动条宽度，避免内容被挤窄。
    /// </summary>
    private void FitBodyToViewport()
    {
        Vector2 content = _bodyContent.GetCombinedMinimumSize();
        float available = Root.GetViewportRect().Size.Y - _titleBar.GetCombinedMinimumSize().Y - ScreenMargin * 2f;
        float height = Math.Min(content.Y, Math.Max(MinBodyHeight, available));
        float scrollBarWidth = content.Y > height ? _body.GetVScrollBar().GetCombinedMinimumSize().X : 0f;
        _body.CustomMinimumSize = new Vector2(content.X + scrollBarWidth, height);
    }

    private Vector2 ClampToViewport(Vector2 position)
    {
        Vector2 viewport = Root.GetViewportRect().Size;
        Vector2 size = Root.Size;
        return new Vector2(
            Math.Clamp(position.X, 0f, Math.Max(0f, viewport.X - size.X)),
            Math.Clamp(position.Y, 0f, Math.Max(0f, viewport.Y - size.Y)));
    }

    private void SaveState()
    {
        _state.X = Root.Position.X;
        _state.Y = Root.Position.Y;
        _state.Save(_statePath);
    }

    /// <summary>
    /// 窗口专用主题：只定义悬停提示（TooltipPanel / TooltipLabel），其余控件仍沿用游戏的项目主题。
    /// Godot 会把提示框挂在发出提示的控件下面，因此窗口内所有控件的提示都使用这里的样式。
    /// </summary>
    private static Theme CreateTheme()
    {
        var tooltip = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.08f, 0.96f),
            BorderColor = new Color(1f, 1f, 1f, 0.16f),
            ShadowColor = new Color(0f, 0f, 0f, 0.4f),
            ShadowSize = 4,
            ContentMarginLeft = 8f,
            ContentMarginRight = 8f,
            ContentMarginTop = 4f,
            ContentMarginBottom = 4f,
        };
        tooltip.SetBorderWidthAll(1);
        tooltip.SetCornerRadiusAll(CornerRadius);

        var theme = new Theme();
        theme.SetStylebox("panel", "TooltipPanel", tooltip);
        theme.SetColor("font_color", "TooltipLabel", TextColor);
        theme.SetFontSize("font_size", "TooltipLabel", 16);
        return theme;
    }

    private static StyleBoxFlat CreateWindowStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.10f, 0.11f, 0.97f),
            BorderColor = new Color(1f, 1f, 1f, 0.12f),
            ShadowColor = new Color(0f, 0f, 0f, 0.45f),
            ShadowSize = 8,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(CornerRadius);
        return style;
    }

    private static StyleBoxFlat CreateTitleStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.17f, 0.17f, 0.19f, 1f),
            ContentMarginLeft = 12f,
            ContentMarginRight = 4f,
            ContentMarginTop = 4f,
            ContentMarginBottom = 4f,
        };
        style.SetCornerRadiusAll(CornerRadius - 1);
        return style;
    }
}
