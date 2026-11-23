using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Melvor2Injector;

/// <summary>
/// 通过 Steam 安装信息自动定位游戏：
/// 注册表中的 Steam 路径 → steamapps\libraryfolders.vdf 列出的所有库 → appmanifest_&lt;AppId&gt;.acf 中的 installdir。
/// </summary>
internal static partial class SteamLocator
{
    /// <summary>Melvor Idle 2 正式版的 Steam AppId。</summary>
    public const string AppId = "3218350";

    private const string DefaultInstallDir = "Melvor Idle 2";

    public static string? FindGameDirectory()
    {
        foreach (string library in GetLibraryFolders())
        {
            string steamApps = Path.Combine(library, "steamapps");
            string manifest = Path.Combine(steamApps, $"appmanifest_{AppId}.acf");

            string? installDir = null;
            if (File.Exists(manifest))
            {
                Match match = InstallDirPattern().Match(SafeRead(manifest));
                installDir = match.Success ? Unescape(match.Groups[1].Value) : null;
            }

            string candidate = Path.Combine(steamApps, "common", installDir ?? DefaultInstallDir);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetLibraryFolders()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? steam = GetSteamPath();
        if (steam is null)
        {
            yield break;
        }

        if (seen.Add(steam))
        {
            yield return steam;
        }

        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            yield break;
        }

        foreach (Match match in LibraryPathPattern().Matches(SafeRead(vdf)))
        {
            string path = Unescape(match.Groups[1].Value);
            if (seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static string? GetSteamPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string? path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    private static string SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    // VDF 中的反斜杠写作 "\\"
    private static string Unescape(string value) => value.Replace(@"\\", @"\");

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathPattern();

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirPattern();
}
