using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.GameMenus;
using GamesByMalcs.Melvor2.GameMenus.DebugPopup;
using Godot;
using Melvor2Patch.Core;

namespace Melvor2Patch.Features.OfficialDebugMenu;

/// <summary>
/// 官方调试菜单（<see cref="DebugMenu"/>）的入口。
/// <para>
/// 发布版的场景里仍然保留了这个节点（<c>GameView.DebugPopup</c>），但 <c>GameView.Bind</c> 不再绑定它，
/// 也没有任何打开它的代码。这里只补上“绑定 + 弹出”这一步，菜单内的功能完全由官方代码执行。
/// </para>
/// </summary>
internal static class OfficialDebugMenu
{
    public static void Open()
    {
        try
        {
            if (!GameSession.TryGet(out GameView? view, out Game? game))
            {
                ModLog.Warn("官方调试菜单只能在游戏内（读档后）打开。");
                return;
            }

            DebugMenu? menu = GetDebugPopup(view);
            if (menu is null || !GodotObject.IsInstanceValid(menu))
            {
                ModLog.Warn("当前 GameView 中没有找到官方调试菜单节点。");
                return;
            }

            // 首次打开或换了存档时，用官方的 Bind 填充菜单数据（物品、货币、敌人等列表）
            if (!ReferenceEquals(GetBoundGame(menu), game))
            {
                menu.Bind(game);
                ModLog.Info("官方调试菜单已绑定到当前存档。");
            }

            menu.PopupCentered();
        }
        catch (Exception ex)
        {
            ModLog.Error("打开官方调试菜单失败。", ex);
        }
    }

    // GameView.DebugPopup 与 DebugMenu.Game 都是私有属性，用 UnsafeAccessor 直接访问，无反射开销
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DebugPopup")]
    private static extern DebugMenu? GetDebugPopup(GameView view);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Game")]
    private static extern Game? GetBoundGame(DebugMenu menu);
}
