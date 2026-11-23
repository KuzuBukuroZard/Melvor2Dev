using Melvor2Patch.Core;

namespace Melvor2Patch.Features.GodMode;

/// <summary>
/// 无敌模式开关。
/// <para>
/// 行为：伤害照常结算（Damaged 事件、伤害统计、任务目标、敌人吸血都按真实伤害进行），
/// 但玩家 HP 在扣血调用内部被回滚，因此不会触发死亡、死亡惩罚和自动进食。
/// 实现见 <see cref="GodModePatches"/>。
/// </para>
/// </summary>
internal static class GodMode
{
    private static volatile bool _enabled;

    /// <summary>开关状态变化后触发（用于持久化）。</summary>
    public static event Action? Changed;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            ModLog.Info($"无敌模式：{(value ? "开启" : "关闭")}");
            Changed?.Invoke();
        }
    }
}
