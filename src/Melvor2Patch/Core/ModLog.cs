using System.Text;

namespace Melvor2Patch.Core;

/// <summary>
/// 线程安全的简单日志：写入 mod 目录下的 Melvor2Patch.log（每次启动覆盖），并同时转发到 Godot 输出。
/// 在 <see cref="Initialize"/> 之前或文件不可写时，只转发到 Godot。
/// </summary>
internal static class ModLog
{
    private const string GodotPrefix = "[Melvor2Patch] ";

    private static readonly Lock Gate = new();
    private static StreamWriter? _writer;

    public static void Initialize(string path)
    {
        lock (Gate)
        {
            try
            {
                _writer?.Dispose();
                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writer = null;
                ForwardToGodot(isError: true, $"无法创建日志文件 {path}：{ex.Message}");
            }
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN ", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        string text = exception is null ? message : message + System.Environment.NewLine + exception;

        lock (Gate)
        {
            try
            {
                _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {text}");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // 日志写入失败不能影响游戏
            }
        }

        ForwardToGodot(level == "ERROR", text);
    }

    private static void ForwardToGodot(bool isError, string text)
    {
        try
        {
            if (isError)
            {
                Godot.GD.PrintErr(GodotPrefix + text);
            }
            else
            {
                Godot.GD.Print(GodotPrefix + text);
            }
        }
        catch (Exception)
        {
            // Godot 原生层尚未就绪或已关闭时忽略
        }
    }
}
