using System.Reflection;
using System.Runtime.Loader;

namespace Melvor2Patch.Core;

/// <summary>
/// 为游戏 ALC 提供 mod 目录下的依赖（0Harmony.dll 等）。
/// 游戏 ALC 只认识游戏 deps.json 里列出的程序集，mod 自带的依赖需要通过 Resolving 事件补充。
/// 必须在任何 Harmony 类型被 JIT 触碰之前注册。
/// </summary>
internal static class DependencyResolver
{
    private static string _directory = string.Empty;
    private static bool _registered;

    public static void Register(AssemblyLoadContext context, string directory)
    {
        if (_registered)
        {
            return;
        }

        _directory = directory;
        context.Resolving += OnResolving;
        _registered = true;
    }

    private static Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name))
        {
            return null;
        }

        string candidate = Path.Combine(_directory, name.Name + ".dll");
        if (!File.Exists(candidate))
        {
            return null;
        }

        ModLog.Info($"依赖解析：{name.FullName} -> {candidate}");
        return context.LoadFromAssemblyPath(candidate);
    }
}
