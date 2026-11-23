using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Melvor2Injector;

internal enum AssemblyState
{
    /// <summary>原版，未注入。</summary>
    Clean,

    /// <summary>已注入引导入口。</summary>
    Patched,
}

/// <summary>对游戏程序集执行注入、还原、状态查询。</summary>
internal static class GameAssemblyPatcher
{
    public static int Patch(GamePaths paths, bool force, bool dryRun, bool showIl)
    {
        RequireFile(paths.GameAssembly);
        AssemblyState current = ReadState(paths.GameAssembly, paths.DataDirectory);

        byte[] source;
        bool sourceIsCurrentFile;
        if (current == AssemblyState.Patched)
        {
            if (!force)
            {
                Console.WriteLine("游戏程序集已经注入过入口，无需重复操作。（如需用新版 Injector 重新注入，请加 --force）");
                PrintModStatus(paths);
                return 0;
            }

            RequireCleanBackup(paths);
            Console.WriteLine("--force：从原版备份重新注入。");
            source = File.ReadAllBytes(paths.BackupAssembly);
            sourceIsCurrentFile = false;
        }
        else
        {
            source = File.ReadAllBytes(paths.GameAssembly);
            sourceIsCurrentFile = true;
        }

        byte[] patched = BuildPatchedAssembly(source, paths.DataDirectory);
        VerifyPatchedAssembly(patched, paths.DataDirectory, showIl);

        if (dryRun)
        {
            Console.WriteLine("dry-run：校验通过，未写入任何文件。");
            return 0;
        }

        if (sourceIsCurrentFile)
        {
            UpdateBackup(paths, source);
        }

        WriteAtomically(paths.GameAssembly, patched);

        if (ReadState(paths.GameAssembly, paths.DataDirectory) != AssemblyState.Patched)
        {
            throw new InjectorException("写入后复查失败：程序集中未检测到引导入口。");
        }

        Console.WriteLine($"注入完成：{paths.GameAssembly}");
        PrintModStatus(paths);
        return 0;
    }

    public static int Restore(GamePaths paths)
    {
        // 先于备份检查：当前已是原版时不需要备份（备份丢失也能正常卸载）；
        // 且可能是游戏更新后的新版本，此时用旧备份覆盖反而会降级
        if (File.Exists(paths.GameAssembly)
            && ReadState(paths.GameAssembly, paths.DataDirectory) == AssemblyState.Clean)
        {
            Console.WriteLine("当前游戏程序集未注入（原版），无需还原。");
            return 0;
        }

        RequireCleanBackup(paths);
        WriteAtomically(paths.GameAssembly, File.ReadAllBytes(paths.BackupAssembly));
        Console.WriteLine($"已从备份还原：{paths.BackupAssembly} -> {paths.GameAssembly}");
        return 0;
    }

    public static int Status(GamePaths paths, bool showIl)
    {
        RequireFile(paths.GameAssembly);
        AssemblyState state = ReadState(paths.GameAssembly, paths.DataDirectory);
        Console.WriteLine($"游戏程序集：{Describe(state)}");

        if (File.Exists(paths.BackupAssembly))
        {
            AssemblyState backupState = ReadState(paths.BackupAssembly, paths.DataDirectory);
            Console.WriteLine($"原版备份：  {Describe(backupState)}（{File.GetLastWriteTime(paths.BackupAssembly):yyyy-MM-dd HH:mm:ss}）");
        }
        else
        {
            Console.WriteLine("原版备份：  不存在");
        }

        PrintModStatus(paths);

        if (showIl && state == AssemblyState.Patched)
        {
            using var resolver = CreateResolver(paths.DataDirectory);
            using var module = ReadModule(File.ReadAllBytes(paths.GameAssembly), resolver);
            PrintIl(module);
        }

        return 0;
    }

    private static byte[] BuildPatchedAssembly(byte[] source, string dataDirectory)
    {
        using var resolver = CreateResolver(dataDirectory);
        using var module = ReadModule(source, resolver);

        // ReadyToRun 镜像：Cecil 只写出 IL，必须清除 ILLibrary 标记，否则运行时会去找不存在的预编译代码
        if ((module.Attributes & ModuleAttributes.ILLibrary) != 0)
        {
            Console.WriteLine("提示：游戏程序集为 ReadyToRun 镜像，注入后将以纯 IL 形式写出。");
            module.Attributes &= ~ModuleAttributes.ILLibrary;
        }

        MethodDefinition bootstrap = BootstrapEmitter.Inject(module);
        Console.WriteLine($"已生成引导方法 {bootstrap.DeclaringType.FullName}::{bootstrap.Name}（{bootstrap.Body.Instructions.Count} 条指令）。");

        using var output = new MemoryStream();
        module.Write(output);
        return output.ToArray();
    }

    /// <summary>重新读取写出的字节并校验，确保最终落盘的内容在游戏运行时中可解析。</summary>
    private static void VerifyPatchedAssembly(byte[] patched, string dataDirectory, bool showIl)
    {
        using var resolver = CreateResolver(dataDirectory);
        using var module = ReadModule(patched, resolver);

        List<string> problems = BootstrapEmitter.Verify(module);
        if (problems.Count > 0)
        {
            throw new InjectorException(
                "注入结果校验失败，以下引用无法在游戏自带的运行时中解析：" + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ", problems));
        }

        Console.WriteLine("校验通过：引导代码中的所有引用均可在游戏自带的运行时中解析。");
        if (showIl)
        {
            PrintIl(module);
        }
    }

    private static void UpdateBackup(GamePaths paths, byte[] cleanBytes)
    {
        if (File.Exists(paths.BackupAssembly))
        {
            if (File.ReadAllBytes(paths.BackupAssembly).AsSpan().SequenceEqual(cleanBytes))
            {
                Console.WriteLine($"原版备份已是最新：{paths.BackupAssembly}");
                return;
            }

            Console.WriteLine("检测到新的原版程序集（游戏可能已更新），覆盖旧备份。");
        }

        WriteAtomically(paths.BackupAssembly, cleanBytes);
        Console.WriteLine($"已备份原版程序集：{paths.BackupAssembly}");
    }

    public static AssemblyState ReadState(string assemblyPath, string dataDirectory)
    {
        using var resolver = CreateResolver(dataDirectory);
        using var module = ReadModule(File.ReadAllBytes(assemblyPath), resolver);
        return BootstrapEmitter.FindBootstrap(module) is null ? AssemblyState.Clean : AssemblyState.Patched;
    }

    /// <summary>只在游戏数据目录中解析引用，确保校验针对的是游戏自带的运行时而不是本机 SDK。</summary>
    private static DefaultAssemblyResolver CreateResolver(string dataDirectory)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (string directory in resolver.GetSearchDirectories())
        {
            resolver.RemoveSearchDirectory(directory);
        }

        resolver.AddSearchDirectory(dataDirectory);
        return resolver;
    }

    private static ModuleDefinition ReadModule(byte[] bytes, IAssemblyResolver resolver) =>
        ModuleDefinition.ReadModule(new MemoryStream(bytes, writable: false), new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadingMode = ReadingMode.Immediate,
            ReadSymbols = false,
        });

    /// <summary>先写临时文件再替换，避免写到一半失败导致游戏程序集损坏。</summary>
    private static void WriteAtomically(string path, byte[] bytes)
    {
        string temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new InjectorException($"写入 {path} 失败：{ex.Message}{Environment.NewLine}游戏是否仍在运行？请关闭游戏后重试。");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理临时文件失败不影响结果
        }
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InjectorException($"文件不存在：{path}");
        }
    }

    private static void RequireCleanBackup(GamePaths paths)
    {
        if (!File.Exists(paths.BackupAssembly))
        {
            throw new InjectorException($"原版备份不存在：{paths.BackupAssembly}。请通过 Steam 验证游戏文件完整性恢复原版后重新注入。");
        }

        if (ReadState(paths.BackupAssembly, paths.DataDirectory) != AssemblyState.Clean)
        {
            throw new InjectorException("原版备份中也检测到了引导入口，备份不可用。请通过 Steam 验证游戏文件完整性恢复原版。");
        }
    }

    private static void PrintModStatus(GamePaths paths)
    {
        Console.WriteLine($"mod 目录：  {paths.ModDirectory}");
        foreach (string file in new[] { GamePaths.ModAssemblyFileName, "0Harmony.dll" })
        {
            string full = Path.Combine(paths.ModDirectory, file);
            Console.WriteLine(File.Exists(full)
                ? $"  [有] {file}（{File.GetLastWriteTime(full):yyyy-MM-dd HH:mm:ss}）"
                : $"  [缺] {file}  <- 运行 install 会从发布包中复制");
        }
    }

    private static void PrintIl(ModuleDefinition module)
    {
        foreach (MethodDefinition? method in new[] { BootstrapEmitter.GetHostMethod(module), BootstrapEmitter.FindBootstrap(module) })
        {
            if (method is null)
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"--- {method.FullName}");
            foreach (Instruction instruction in method.Body.Instructions)
            {
                Console.WriteLine($"    {instruction}");
            }

            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
            {
                Console.WriteLine(
                    $"    .{handler.HandlerType.ToString().ToLowerInvariant()} {handler.CatchType?.FullName ?? ""} "
                    + $"try IL_{handler.TryStart.Offset:x4}..IL_{handler.TryEnd?.Offset:x4} "
                    + $"handler IL_{handler.HandlerStart.Offset:x4}..IL_{handler.HandlerEnd?.Offset:x4}");
            }
        }
    }

    private static string Describe(AssemblyState state) => state switch
    {
        AssemblyState.Clean => "原版（未注入）",
        AssemblyState.Patched => "已注入",
        _ => state.ToString(),
    };
}
