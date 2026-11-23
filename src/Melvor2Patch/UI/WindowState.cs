using System.Text.Json;
using Melvor2Patch.Core;

namespace Melvor2Patch.UI;

/// <summary>
/// 窗口的位置与折叠状态，保存在 mod 目录下的 ui-state.json。
/// 功能开关由 <see cref="Features.FeatureSettings"/> 单独保存在 settings.json。
/// </summary>
internal sealed class WindowState
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public float? X { get; set; }

    public float? Y { get; set; }

    public bool Collapsed { get; set; } = true;

    public static WindowState Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<WindowState>(File.ReadAllText(path), Options) ?? new WindowState()
                : new WindowState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ModLog.Warn($"读取窗口状态失败，使用默认布局：{ex.Message}");
            return new WindowState();
        }
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModLog.Warn($"保存窗口状态失败：{ex.Message}");
        }
    }
}
