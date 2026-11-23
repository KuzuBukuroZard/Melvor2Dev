using System.Diagnostics.CodeAnalysis;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.GameMenus;
using Godot;
using HarmonyLib;

namespace Melvor2Patch.Core;

/// <summary>
/// 当前游戏会话（已读档并进入游戏界面）的跟踪，供各功能获取当前的 <see cref="Game"/> 与 <see cref="GameView"/>。
/// <para>
/// 会话开始：<c>GameView.Bind(Game)</c>。注意它在读档的后台线程（<c>LoadNextState</c> 的 Task.Run）中调用，
/// 因此这里只做线程安全的记录，不触碰任何场景树中的节点。
/// </para>
/// <para>
/// 会话结束：游戏返回主菜单时不会调用 <c>GameView.Unbind</c>（它只在 Bind 内部调用），而是直接释放视图，
/// 因此以视图离开场景树为准。离线进度运行期间视图一直保留在场景树中，会话保持有效。
/// </para>
/// 补丁只做记录，不改变 GameView 的任何行为。
/// </summary>
internal static class GameSession
{
    private sealed record Session(GameView View, Game Game);

    private static Session? _current;

    /// <summary>只能在主线程调用。</summary>
    public static bool TryGet([NotNullWhen(true)] out GameView? view, [NotNullWhen(true)] out Game? game)
    {
        Session? session = Volatile.Read(ref _current);
        if (session is not null && GodotObject.IsInstanceValid(session.View))
        {
            view = session.View;
            game = session.Game;
            return true;
        }

        view = null;
        game = null;
        return false;
    }

    [HarmonyPatch(typeof(GameView), nameof(GameView.Bind), [typeof(Game)])]
    private static class TrackGameView
    {
        private static void Postfix(GameView __instance, Game game)
        {
            var session = new Session(__instance, game);
            Volatile.Write(ref _current, session);

            // 视图此时尚未加入场景树，在后台线程上连接信号是安全的
            __instance.TreeExiting += () => Interlocked.CompareExchange(ref _current, null, session);
        }
    }
}
