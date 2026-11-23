using System.Reflection;
using System.Runtime.Loader;

namespace Melvor2Patch.Core;

/// <summary>mod 运行时的全局上下文：身份信息、路径、游戏程序集与其 AssemblyLoadContext。</summary>
internal static class ModContext
{
    public const string ModId = "melvor2patch";
    public const string ModName = "Melvor2Patch";
    public const string LogFileName = "Melvor2Patch.log";

    public static Assembly ModAssembly { get; } = typeof(ModContext).Assembly;

    public static string Version { get; } = ModAssembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>游戏程序集 GamesByMalcs.Melvor2（与 mod 位于同一 ALC，typeof 得到的就是游戏正在使用的那一份）。</summary>
    public static Assembly GameAssembly { get; private set; } = null!;

    /// <summary>游戏程序集所在的 AssemblyLoadContext（Godot 通过 hostfxr 组件方式加载，通常不是 Default）。</summary>
    public static AssemblyLoadContext GameLoadContext { get; private set; } = null!;

    /// <summary>mod 所在目录：&lt;游戏根目录&gt;\Melvor2Patch\。</summary>
    public static string ModDirectory { get; private set; } = string.Empty;

    public static void Initialize()
    {
        GameAssembly = typeof(GamesByMalcs.Melvor2.Main).Assembly;
        GameLoadContext = AssemblyLoadContext.GetLoadContext(GameAssembly) ?? AssemblyLoadContext.Default;
        ModDirectory = Path.GetDirectoryName(ModAssembly.Location) ?? AppContext.BaseDirectory;
    }

    /// <summary>拼接 mod 目录下的路径。</summary>
    public static string GetPath(params string[] parts) => Path.Combine([ModDirectory, .. parts]);
}
