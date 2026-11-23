using System.Diagnostics.CodeAnalysis;

namespace Melvor2Injector;

/// <summary>可预期的注入错误，只输出消息，不输出堆栈。</summary>
internal sealed class InjectorException(string message) : Exception(message);

/// <summary>游戏安装相关的路径集合。</summary>
internal sealed record GamePaths(
    string GameRoot,
    string DataDirectory,
    string GameAssembly,
    string BackupAssembly,
    string ModDirectory)
{
    public const string GameAssemblyFileName = "GamesByMalcs.Melvor2.dll";
    public const string BackupSuffix = ".bak";
    public const string ModDirectoryName = "Melvor2Patch";
    public const string ModAssemblyFileName = "Melvor2Patch.dll";

    public string ModAssembly => Path.Combine(ModDirectory, ModAssemblyFileName);

    /// <summary>
    /// 接受游戏根目录、data_* 数据目录或 GamesByMalcs.Melvor2.dll 本身的路径。
    /// </summary>
    public static GamePaths Resolve(string input)
    {
        string full = Path.GetFullPath(input.Trim().Trim('"'));
        string dataDirectory = FindDataDirectory(full)
            ?? throw new InjectorException(
                $"无法在 \"{full}\" 下找到 {GameAssemblyFileName}。请用 --game 指定游戏根目录（包含 Melvor2.exe 的目录）。");

        string gameRoot = Path.GetDirectoryName(dataDirectory)
            ?? throw new InjectorException($"无法确定游戏根目录：{dataDirectory}");

        return new GamePaths(
            gameRoot,
            dataDirectory,
            Path.Combine(dataDirectory, GameAssemblyFileName),
            Path.Combine(dataDirectory, GameAssemblyFileName + BackupSuffix),
            Path.Combine(gameRoot, ModDirectoryName));
    }

    /// <summary>未指定游戏目录时，依次使用环境变量 MELVOR2_GAME_DIR 和 Steam 安装信息自动定位。</summary>
    public static bool TryDetect([NotNullWhen(true)] out GamePaths? paths)
    {
        paths = null;
        string? fromEnvironment = Environment.GetEnvironmentVariable("MELVOR2_GAME_DIR");
        string? candidate = !string.IsNullOrWhiteSpace(fromEnvironment) ? fromEnvironment : SteamLocator.FindGameDirectory();
        if (candidate is null)
        {
            return false;
        }

        try
        {
            paths = Resolve(candidate);
            return true;
        }
        catch (InjectorException)
        {
            return false;
        }
    }

    private static string? FindDataDirectory(string path)
    {
        if (File.Exists(path))
        {
            return string.Equals(Path.GetFileName(path), GameAssemblyFileName, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(path)
                : null;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        if (File.Exists(Path.Combine(path, GameAssemblyFileName)))
        {
            return path;
        }

        // Godot 导出的数据目录命名为 data_<程序集名>_<平台>_<架构>
        return Directory.EnumerateDirectories(path, "data_*")
            .FirstOrDefault(d => File.Exists(Path.Combine(d, GameAssemblyFileName)));
    }
}
