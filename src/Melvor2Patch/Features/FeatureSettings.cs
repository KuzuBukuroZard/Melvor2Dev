using System.Text.Json;
using Melvor2Patch.Core;
using Melvor2Patch.Features.InfiniteConsumables;
using Consumables = Melvor2Patch.Features.InfiniteConsumables.InfiniteConsumables;
using GodModeFeature = Melvor2Patch.Features.GodMode.GodMode;
using TimeMachineFeature = Melvor2Patch.Features.TimeMachine.TimeMachine;

namespace Melvor2Patch.Features;

/// <summary>
/// 功能配置的持久化，保存在 mod 目录下的 settings.json。
/// <para>
/// 在引导阶段加载并直接应用到各功能（纯托管操作，不涉及 Godot 对象），
/// 因此补丁从游戏一启动就按保存的状态生效，读档时的离线结算也包括在内。
/// 之后订阅各功能的 Changed 事件，任何途径的修改（UI 勾选、All/None 等）都会自动保存。
/// </para>
/// </summary>
internal static class FeatureSettings
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string _path = string.Empty;
    private static bool _loaded;
    private static double _timeSkipHours = 1.0;

    /// <summary>时光机上次使用的时长（小时），仅供 UI 记忆输入值。</summary>
    public static double TimeSkipHours
    {
        get => _timeSkipHours;
        set
        {
            value = Math.Clamp(value, TimeMachineFeature.MinHours, TimeMachineFeature.MaxHours);
            if (_timeSkipHours.Equals(value))
            {
                return;
            }

            _timeSkipHours = value;
            Save();
        }
    }

    public static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _path = ModContext.GetPath(FileName);
        SettingsData data = Read(_path);

        GodModeFeature.Enabled = data.GodMode;
        foreach (string name in data.NoConsumption)
        {
            // 旧版的 Bait / Runes 会展开为拆分后的两个类别，下次保存时写入新名称
            if (Consumables.TryParseSavedName(name, out IReadOnlyList<ConsumableCategory> categories))
            {
                foreach (ConsumableCategory category in categories)
                {
                    Consumables.SetEnabled(category, true);
                }
            }
            else
            {
                ModLog.Warn($"配置中的未知消耗品类别已忽略：{name}");
            }
        }

        _timeSkipHours = Math.Clamp(data.TimeSkipHours, TimeMachineFeature.MinHours, TimeMachineFeature.MaxHours);

        // 先应用再订阅，加载过程本身不会触发保存
        GodModeFeature.Changed += Save;
        Consumables.Changed += Save;
        _loaded = true;

        ModLog.Info(File.Exists(_path) ? $"配置已加载：{_path}" : "未找到配置文件，使用默认配置（修改任一开关后自动创建）。");
    }

    private static void Save()
    {
        if (!_loaded)
        {
            return;
        }

        var data = new SettingsData
        {
            GodMode = GodModeFeature.Enabled,
            NoConsumption = Consumables.Categories
                .Where(Consumables.IsEnabled)
                .Select(c => c.ToString())
                .ToList(),
            TimeSkipHours = _timeSkipHours,
        };

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(data, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModLog.Warn($"保存配置失败：{ex.Message}");
        }
    }

    private static SettingsData Read(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(path), Options) ?? new SettingsData()
                : new SettingsData();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ModLog.Warn($"读取配置失败，使用默认配置：{ex.Message}");
            return new SettingsData();
        }
    }
}

/// <summary>settings.json 的结构。消耗品类别按名称保存，增删类别时不会错位。</summary>
internal sealed class SettingsData
{
    public bool GodMode { get; set; }

    public List<string> NoConsumption { get; set; } = [];

    public double TimeSkipHours { get; set; } = 1.0;
}
