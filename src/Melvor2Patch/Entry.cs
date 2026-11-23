using System.Runtime.CompilerServices;
using Melvor2Patch.Core;
using Melvor2Patch.Diagnostics;
using Melvor2Patch.Features;

namespace Melvor2Patch;

/// <summary>
/// mod 入口。由注入到游戏程序集中的引导方法 <c>GodotPlugins.Game.Main.Melvor2Patch_Bootstrap</c> 通过反射调用。
/// <para>
/// 生命周期分两个阶段：
/// <list type="number">
/// <item>
/// <b>引导阶段</b>（<see cref="Bootstrap"/>）：位于 <c>InitializeFromGameProject</c> 末尾，场景树和 Autoload 尚未创建，
/// Harmony 补丁会先于任何游戏逻辑生效。
/// <b>此阶段禁止访问任何 Godot 对象或单例</b>（Engine、OS、SceneTree、new Node() 等）：
/// 原生端尚未接收托管回调表，访问会触发无法捕获的访问违例（0xC0000005）直接杀死进程。
/// 只允许纯托管逻辑、Harmony 补丁和 GD.Print 这类纯原生函数调用。
/// </item>
/// <item>
/// <b>游戏就绪阶段</b>（<see cref="OnMainReady"/>）：由 Main._Ready 的补丁触发，可以使用完整的 Godot API（UI 等）。
/// </item>
/// </list>
/// </para>
/// </summary>
public static class Entry
{
    private static int _bootstrapped;
    private static int _mainReady;

    /// <summary>引导代码按名称查找此方法，签名不要修改：public static void Bootstrap()。</summary>
    public static void Bootstrap()
    {
        if (Interlocked.Exchange(ref _bootstrapped, 1) != 0)
        {
            return;
        }

        try
        {
            ModContext.Initialize();
            ModLog.Initialize(ModContext.GetPath(ModContext.LogFileName));
            ModLog.Info($"{ModContext.ModName} {ModContext.Version} 启动");

            // 必须先注册依赖解析，再触碰任何 Harmony 类型
            DependencyResolver.Register(ModContext.GameLoadContext, ModContext.ModDirectory);
            Start();
        }
        catch (Exception ex)
        {
            ModLog.Error("初始化失败，mod 未生效。", ex);
        }
    }

    // 单独成方法并禁止内联：JIT 编译此方法时才会解析 Harmony 等依赖，此时 Resolving 事件已注册
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start()
    {
        // 先恢复功能开关，再应用补丁：保证从游戏第一帧起（包括读档时的离线结算）就按保存的状态生效。
        // 配置出错只丢配置，不能连累补丁
        try
        {
            FeatureSettings.Load();
        }
        catch (Exception ex)
        {
            ModLog.Error("加载配置失败，使用默认配置。", ex);
        }

        PatchManager.ApplyAll();
        MetadataDumper.RunIfRequested();
    }

    /// <summary>游戏主节点就绪后调用一次，此时可以安全使用 Godot API。</summary>
    internal static void OnMainReady(GamesByMalcs.Melvor2.Main main)
    {
        if (Interlocked.Exchange(ref _mainReady, 1) != 0)
        {
            return;
        }

        try
        {
            UI.ModMenu.Attach(main);
        }
        catch (Exception ex)
        {
            ModLog.Error("游戏就绪阶段初始化失败。", ex);
        }
    }
}
