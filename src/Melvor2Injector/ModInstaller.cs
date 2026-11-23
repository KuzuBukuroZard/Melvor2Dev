using System.Diagnostics;

namespace Melvor2Injector;

/// <summary>
/// 面向普通用户的安装 / 卸载：在注入之外，负责把发布包里的 mod 文件复制到游戏目录。
/// 发布包结构：Melvor2Injector.exe 同目录下的 Melvor2Patch\ 文件夹（Melvor2Patch.dll、0Harmony.dll）。
/// </summary>
internal static class ModInstaller
{
    /// <summary>发布包中 mod 文件所在目录（exe 同目录下的 Melvor2Patch\）。</summary>
    public static string PackageModDirectory { get; } = Path.Combine(AppContext.BaseDirectory, GamePaths.ModDirectoryName);

    public static bool HasPackagedMod => File.Exists(Path.Combine(PackageModDirectory, GamePaths.ModAssemblyFileName));

    /// <summary>复制 mod 文件并注入入口。已注入时只更新 mod 文件。</summary>
    public static int Install(GamePaths paths, bool dryRun)
    {
        EnsureGameNotRunning();

        if (HasPackagedMod)
        {
            CopyModFiles(paths, dryRun);
        }
        else if (!File.Exists(paths.ModAssembly))
        {
            throw new InjectorException(
                $"找不到 mod 文件：{Path.Combine(PackageModDirectory, GamePaths.ModAssemblyFileName)}。" +
                "请确认 Melvor2Injector.exe 与 Melvor2Patch 文件夹位于同一目录（不要单独移动 exe）。");
        }
        else
        {
            Console.WriteLine("未找到发布包中的 mod 文件，保留游戏目录中现有的 mod 文件。");
        }

        Console.WriteLine();
        return GameAssemblyPatcher.Patch(paths, force: false, dryRun, showIl: false);
    }

    /// <summary>还原原版游戏程序集；可选删除游戏目录中的 mod 文件夹（含配置）。</summary>
    public static int Uninstall(GamePaths paths, bool removeModFiles)
    {
        EnsureGameNotRunning();

        int result = GameAssemblyPatcher.Restore(paths);
        if (result != 0)
        {
            return result;
        }

        if (removeModFiles && Directory.Exists(paths.ModDirectory))
        {
            Directory.Delete(paths.ModDirectory, recursive: true);
            Console.WriteLine($"已删除 mod 目录：{paths.ModDirectory}");
        }

        Console.WriteLine("卸载完成。");
        return 0;
    }

    /// <summary>复制发布包中的全部文件，覆盖同名文件；游戏目录里的配置文件（settings.json 等）不在包内，不受影响。</summary>
    private static void CopyModFiles(GamePaths paths, bool dryRun)
    {
        string[] files = Directory.GetFiles(PackageModDirectory);
        Console.WriteLine($"{(dryRun ? "dry-run：将复制" : "复制")} mod 文件 -> {paths.ModDirectory}");

        if (!dryRun)
        {
            Directory.CreateDirectory(paths.ModDirectory);
        }

        foreach (string source in files)
        {
            string name = Path.GetFileName(source);
            if (!dryRun)
            {
                try
                {
                    File.Copy(source, Path.Combine(paths.ModDirectory, name), overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new InjectorException($"复制 {name} 失败：{ex.Message}");
                }
            }

            Console.WriteLine($"  {name}");
        }
    }

    private static void EnsureGameNotRunning()
    {
        if (Process.GetProcessesByName("Melvor2").Length > 0)
        {
            throw new InjectorException("游戏正在运行，请先关闭游戏再操作。");
        }
    }
}
