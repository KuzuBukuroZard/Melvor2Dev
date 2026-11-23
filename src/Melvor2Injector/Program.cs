using System.Reflection;
using Melvor2Injector;

string version = typeof(SteamLocator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";
bool interactive = args.Length == 0 && !Console.IsInputRedirected;

int exitCode;
try
{
    exitCode = interactive ? Interactive.Run(version) : Cli.Run(args);
}
catch (InjectorException ex)
{
    Console.Error.WriteLine($"错误：{ex.Message}");
    exitCode = 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"未预期的错误：{ex}");
    exitCode = 2;
}

if (interactive)
{
    Console.WriteLine();
    Console.WriteLine("按任意键退出...");
    Console.ReadKey(intercept: true);
}

return exitCode;

/// <summary>命令行模式。</summary>
internal static class Cli
{
    public static int Run(string[] args)
    {
        string command = "install";
        string? gameDirectory = null;
        bool force = false, dryRun = false, showIl = false, removeFiles = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "install" or "uninstall" or "status" or "patch" or "restore":
                    command = arg;
                    break;
                case "help" or "-h" or "--help" or "/?":
                    Console.WriteLine(UsageText.Value);
                    return 0;
                case "-g" or "--game":
                    gameDirectory = i + 1 < args.Length ? args[++i] : throw new InjectorException($"{arg} 需要一个路径参数。");
                    break;
                case "-f" or "--force":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--show-il":
                    showIl = true;
                    break;
                case "--remove-files":
                    removeFiles = true;
                    break;
                default:
                    throw new InjectorException($"未知参数：{arg}（使用 help 查看用法）");
            }
        }

        GamePaths paths = Locate(gameDirectory);
        PrintHeader(paths);

        return command switch
        {
            "install" => ModInstaller.Install(paths, dryRun),
            "uninstall" => ModInstaller.Uninstall(paths, removeFiles),
            "status" => GameAssemblyPatcher.Status(paths, showIl),
            "restore" => GameAssemblyPatcher.Restore(paths),
            _ => GameAssemblyPatcher.Patch(paths, force, dryRun, showIl),
        };
    }

    public static GamePaths Locate(string? gameDirectory)
    {
        if (gameDirectory is not null)
        {
            return GamePaths.Resolve(gameDirectory);
        }

        return GamePaths.TryDetect(out GamePaths? detected)
            ? detected
            : throw new InjectorException("未能自动找到游戏目录。请使用 --game <路径> 指定（包含 Melvor2.exe 的目录），或设置环境变量 MELVOR2_GAME_DIR。");
    }

    public static void PrintHeader(GamePaths paths)
    {
        Console.WriteLine($"游戏目录：  {paths.GameRoot}");
        Console.WriteLine($"游戏程序集：{paths.GameAssembly}");
        Console.WriteLine();
    }
}

/// <summary>双击运行时的交互菜单。</summary>
internal static class Interactive
{
    public static int Run(string version)
    {
        Console.WriteLine($"Melvor2Patch 安装器 v{version}");
        Console.WriteLine(new string('=', 40));

        GamePaths paths = LocateInteractively();
        Console.WriteLine();
        Cli.PrintHeader(paths);
        GameAssemblyPatcher.Status(paths, showIl: false);

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("请选择：");
            Console.WriteLine("  [1] 安装 / 更新");
            Console.WriteLine("  [2] 卸载");
            Console.WriteLine("  [3] 退出");
            Console.Write("> ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1":
                    Console.WriteLine();
                    int installed = ModInstaller.Install(paths, dryRun: false);
                    if (installed == 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("安装完成。启动游戏后，屏幕左侧会出现 Melvor2Patch 窗口。");
                    }

                    return installed;

                case "2":
                    Console.Write("是否同时删除 mod 文件夹（包括你的设置）？[y/N] ");
                    bool remove = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine();
                    return ModInstaller.Uninstall(paths, remove);

                case "3" or "" or null:
                    return 0;

                default:
                    Console.WriteLine("请输入 1、2 或 3。");
                    break;
            }
        }
    }

    /// <summary>先自动查找；找不到时让用户输入或拖入游戏文件夹，直到路径有效或用户放弃。</summary>
    private static GamePaths LocateInteractively()
    {
        if (GamePaths.TryDetect(out GamePaths? detected))
        {
            Console.WriteLine("已自动找到游戏。");
            return detected;
        }

        Console.WriteLine("未能自动找到 Melvor Idle 2。");
        while (true)
        {
            Console.WriteLine("请把游戏文件夹（包含 Melvor2.exe 的目录）拖入本窗口，或粘贴路径后按回车（直接回车退出）：");
            Console.Write("> ");
            string? input = Console.ReadLine()?.Trim().Trim('"');
            if (string.IsNullOrEmpty(input))
            {
                throw new InjectorException("已取消。");
            }

            try
            {
                return GamePaths.Resolve(input);
            }
            catch (InjectorException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}

/// <summary>命令行帮助文本。</summary>
internal static class UsageText
{
    public const string Value = """
        Melvor2Injector - Melvor Idle 2 的 Melvor2Patch 安装器

        双击运行：进入交互菜单（自动查找游戏，一键安装 / 卸载）。

        命令行用法：
          Melvor2Injector [命令] [选项]

        命令：
          install    复制 mod 文件到游戏目录并注入加载入口（推荐，默认）
          uninstall  还原原版游戏程序集（加 --remove-files 同时删除 mod 文件夹与配置）
          status     查看注入状态与 mod 文件
          patch      只注入加载入口，不复制 mod 文件（开发用）
          restore    只还原原版游戏程序集（开发用）
          help       显示本帮助

        选项：
          -g, --game <路径>  游戏根目录 / data_* 目录 / GamesByMalcs.Melvor2.dll 路径
                             未指定时依次使用环境变量 MELVOR2_GAME_DIR、Steam 安装信息自动查找
          -f, --force        patch：已注入时从原版备份重新注入（更新 Injector 后使用）
              --dry-run      install / patch：只校验，不写入任何文件
              --show-il      patch / status：打印入口方法与引导方法的 IL
              --remove-files uninstall：同时删除游戏目录下的 Melvor2Patch 文件夹（含配置）
        """;
}
