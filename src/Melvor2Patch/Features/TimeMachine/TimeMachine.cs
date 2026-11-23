using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.GameMenus;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.TimeMachine;

/// <summary>
/// 时光机：跳过一段时间，并由游戏原版的离线进度流程完成结算。
/// <para>
/// 实现上只调用公开 API <see cref="Game.RequestOfflineHours"/>（官方调试菜单的 Run Offline 也是它）：
/// Game.OfflineRequested → Main.GameState.GameOfflineRequestedHandler → RunOfflineProgress
/// （记录离线快照 → 暂停场景树 → 后台逐 tick 执行 Game.PropagateTick → 弹出离线结算并写入事件日志）。
/// </para>
/// <para>
/// 正式版的变化（相对 Demo）：
/// <list type="bullet">
/// <item>GameState 改为 RunningMode 状态机，只有处于正常运行（Process）时才接受请求，离线结算进行中的请求会被游戏忽略。</item>
/// <item>这条路径不再调用 Game.OnOfflineProgressStart（只有系统休眠超过 60 秒的路径会调用），
/// 离线标记保持为 false，因此跳过的时间计入在线时间统计（与正式版读档时的离线结算一致）。</item>
/// </list>
/// 资源、经验等统计仍由正常 tick 产生。
/// </para>
/// <para>
/// 不修改官方的最大离线时长：游戏会把请求截断到该上限（默认 24 小时，可被加成提升），
/// 因此这里也把单次请求限制在 <see cref="MaxHours"/> 以内。
/// </para>
/// </summary>
internal static class TimeMachine
{
    public const double MinHours = 0.5;
    public const double MaxHours = 24.0;

    public static void Skip(double hours)
    {
        try
        {
            if (!GameSession.TryGet(out GameView? _, out Game? game))
            {
                ModLog.Warn("时光机只能在游戏内（读档后）使用。");
                return;
            }

            hours = Math.Clamp(hours, MinHours, MaxHours);
            ModLog.Info($"时光机：请求跳过 {hours:0.##} 小时，交由游戏离线进度结算。");
            game.RequestOfflineHours(hours);
        }
        catch (Exception ex)
        {
            ModLog.Error("时光机执行失败。", ex);
        }
    }
}
