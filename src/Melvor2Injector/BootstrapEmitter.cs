using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Melvor2Injector;

/// <summary>
/// 在 <c>GodotPlugins.Game.Main</c> 中生成引导方法，并在 <c>InitializeFromGameProject</c> 中插入受保护的调用。
/// <para>
/// 插入点位于 <c>ScriptManagerBridge.LookupScriptsInAssembly(...)</c> 之后：此时 Godot 的托管桥接已初始化
/// （可以调用 Godot API），但场景树与 Autoload 尚未创建，Harmony 补丁可以在任何游戏逻辑运行前生效。
/// </para>
/// 生成的引导方法等价于：
/// <code>
/// private static void Melvor2Patch_Bootstrap()
/// {
///     try
///     {
///         Assembly game = typeof(GodotPlugins.Game.Main).Assembly;
///         string path = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(game.Location)), "Melvor2Patch", "Melvor2Patch.dll");
///         if (!File.Exists(path)) { GD.Print("[Melvor2Patch] ... " + path); return; }
///         // 加载到游戏所在的 ALC，保证 mod 中的 typeof(...) 与游戏是同一份类型
///         AssemblyLoadContext.GetLoadContext(game).LoadFromAssemblyPath(path)
///             .GetType("Melvor2Patch.Entry", true).GetMethod("Bootstrap").Invoke(null, null);
///     }
///     catch (Exception ex)
///     {
///         GD.PrintErr("[Melvor2Patch] ... " + ex);
///     }
/// }
/// </code>
/// </summary>
internal static class BootstrapEmitter
{
    public const string HostTypeName = "GodotPlugins.Game.Main";
    public const string HostMethodName = "InitializeFromGameProject";
    public const string BootstrapMethodName = "Melvor2Patch_Bootstrap";

    private const string AnchorTypeName = "Godot.Bridge.ScriptManagerBridge";
    private const string AnchorMethodName = "LookupScriptsInAssembly";

    private const string ModEntryTypeName = "Melvor2Patch.Entry";
    private const string ModEntryMethodName = "Bootstrap";

    public static TypeDefinition GetHostType(ModuleDefinition module) =>
        module.GetType(HostTypeName)
        ?? throw new InjectorException($"找不到入口类型 {HostTypeName}，游戏的 Godot 入口可能已经变化。");

    public static MethodDefinition GetHostMethod(ModuleDefinition module) =>
        GetHostType(module).Methods.FirstOrDefault(m => m.Name == HostMethodName && m.HasBody)
        ?? throw new InjectorException($"找不到入口方法 {HostTypeName}.{HostMethodName}，游戏的 Godot 入口可能已经变化。");

    public static MethodDefinition? FindBootstrap(ModuleDefinition module) =>
        GetHostType(module).Methods.FirstOrDefault(m => m.Name == BootstrapMethodName);

    /// <summary>向模块注入引导方法和调用点，返回生成的引导方法。</summary>
    public static MethodDefinition Inject(ModuleDefinition module)
    {
        if (FindBootstrap(module) is not null)
        {
            throw new InjectorException("程序集中已存在引导方法，不能重复注入。");
        }

        TypeDefinition host = GetHostType(module);
        MethodDefinition hostMethod = GetHostMethod(module);
        var refs = new References(module);

        MethodDefinition bootstrap = CreateBootstrapMethod(module, host, refs);
        host.Methods.Add(bootstrap);
        InsertGuardedCall(module, hostMethod, bootstrap);
        return bootstrap;
    }

    /// <summary>
    /// 校验注入结果：调用点存在，且引导方法用到的所有类型/成员都能在游戏自带的运行时中解析。
    /// 返回问题列表，为空表示通过。
    /// </summary>
    public static List<string> Verify(ModuleDefinition module)
    {
        var problems = new List<string>();
        MethodDefinition? bootstrap = FindBootstrap(module);
        if (bootstrap is null)
        {
            problems.Add($"缺少引导方法 {BootstrapMethodName}");
            return problems;
        }

        MethodDefinition hostMethod = GetHostMethod(module);
        bool called = hostMethod.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is MethodReference m && m.Name == BootstrapMethodName);
        if (!called)
        {
            problems.Add($"{HostMethodName} 中没有对 {BootstrapMethodName} 的调用");
        }

        foreach (Instruction instruction in bootstrap.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case MethodReference method:
                    CheckResolvable(method.FullName, method.Resolve, problems);
                    break;
                case TypeReference type:
                    CheckResolvable(type.FullName, type.Resolve, problems);
                    break;
            }
        }

        foreach (VariableDefinition variable in bootstrap.Body.Variables)
        {
            CheckResolvable(variable.VariableType.FullName, variable.VariableType.Resolve, problems);
        }

        foreach (ExceptionHandler handler in bootstrap.Body.ExceptionHandlers.Concat(hostMethod.Body.ExceptionHandlers))
        {
            if (handler.CatchType is { } catchType)
            {
                CheckResolvable(catchType.FullName, catchType.Resolve, problems);
            }
        }

        return problems;
    }

    private static void CheckResolvable(string name, Func<IMemberDefinition?> resolve, List<string> problems)
    {
        try
        {
            if (resolve() is null)
            {
                problems.Add(name);
            }
        }
        catch (AssemblyResolutionException ex)
        {
            problems.Add($"{name}（找不到程序集 {ex.AssemblyReference.FullName}）");
        }
    }

    private static MethodDefinition CreateBootstrapMethod(ModuleDefinition module, TypeDefinition host, References refs)
    {
        TypeSystem ts = module.TypeSystem;
        var method = new MethodDefinition(
            BootstrapMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            ts.Void);

        MethodBody body = method.Body;
        body.InitLocals = true;
        var gameAssembly = new VariableDefinition(refs.Assembly);
        var entryPath = new VariableDefinition(ts.String);
        var exception = new VariableDefinition(refs.Exception);
        body.Variables.Add(gameAssembly);
        body.Variables.Add(entryPath);
        body.Variables.Add(exception);

        ILProcessor il = body.GetILProcessor();
        Instruction ret = il.Create(OpCodes.Ret);
        Instruction notFound = il.Create(OpCodes.Ldstr, "[Melvor2Patch] 未找到 mod 程序集，跳过加载：");
        Instruction handlerStart = il.Create(OpCodes.Stloc, exception);

        // try {
        Instruction tryStart = il.Create(OpCodes.Ldtoken, host);
        il.Append(tryStart);
        il.Emit(OpCodes.Call, refs.GetTypeFromHandle);
        il.Emit(OpCodes.Callvirt, refs.TypeGetAssembly);
        il.Emit(OpCodes.Stloc, gameAssembly);

        // path = Path.Combine(<游戏根目录>, "Melvor2Patch", "Melvor2Patch.dll")
        il.Emit(OpCodes.Ldloc, gameAssembly);
        il.Emit(OpCodes.Callvirt, refs.AssemblyGetLocation);
        il.Emit(OpCodes.Call, refs.PathGetDirectoryName); // data_* 目录
        il.Emit(OpCodes.Call, refs.PathGetDirectoryName); // 游戏根目录
        il.Emit(OpCodes.Ldstr, GamePaths.ModDirectoryName);
        il.Emit(OpCodes.Ldstr, GamePaths.ModAssemblyFileName);
        il.Emit(OpCodes.Call, refs.PathCombine3);
        il.Emit(OpCodes.Stloc, entryPath);

        il.Emit(OpCodes.Ldloc, entryPath);
        il.Emit(OpCodes.Call, refs.FileExists);
        il.Emit(OpCodes.Brfalse, notFound);

        // AssemblyLoadContext.GetLoadContext(game).LoadFromAssemblyPath(path)
        il.Emit(OpCodes.Ldloc, gameAssembly);
        il.Emit(OpCodes.Call, refs.GetLoadContext);
        il.Emit(OpCodes.Ldloc, entryPath);
        il.Emit(OpCodes.Callvirt, refs.LoadFromAssemblyPath);

        // .GetType("Melvor2Patch.Entry", true).GetMethod("Bootstrap").Invoke(null, null)
        il.Emit(OpCodes.Ldstr, ModEntryTypeName);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, refs.AssemblyGetType);
        il.Emit(OpCodes.Ldstr, ModEntryMethodName);
        il.Emit(OpCodes.Callvirt, refs.TypeGetMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, refs.MethodBaseInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, ret);

        // 未找到 mod：打印提示后正常返回
        il.Append(notFound);
        il.Emit(OpCodes.Ldloc, entryPath);
        il.Emit(OpCodes.Call, refs.StringConcat2);
        il.Emit(OpCodes.Call, refs.GdPrint);
        il.Emit(OpCodes.Leave, ret);
        // }

        // catch (Exception ex) { GD.PrintErr(... + ex.ToString()); }
        il.Append(handlerStart);
        il.Emit(OpCodes.Ldstr, "[Melvor2Patch] 引导失败：");
        il.Emit(OpCodes.Ldloc, exception);
        il.Emit(OpCodes.Callvirt, refs.ObjectToString);
        il.Emit(OpCodes.Call, refs.StringConcat2);
        il.Emit(OpCodes.Call, refs.GdPrintErr);
        il.Emit(OpCodes.Leave, ret);

        il.Append(ret);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = ret,
            CatchType = refs.Exception,
        });

        body.OptimizeMacros();
        return method;
    }

    /// <summary>
    /// 在锚点之后插入 <c>try { Melvor2Patch_Bootstrap(); } catch (object) { }</c>。
    /// 外层再包一次是为了防止引导方法本身 JIT/类型加载失败时把异常抛给 Godot，导致整个 C# 层初始化失败。
    /// </summary>
    private static void InsertGuardedCall(ModuleDefinition module, MethodDefinition hostMethod, MethodDefinition bootstrap)
    {
        MethodBody body = hostMethod.Body;
        body.SimplifyMacros();

        Instruction anchor = body.Instructions.FirstOrDefault(i =>
                i.OpCode == OpCodes.Call
                && i.Operand is MethodReference m
                && m.Name == AnchorMethodName
                && m.DeclaringType.FullName == AnchorTypeName
                && m.ReturnType.MetadataType == MetadataType.Void)
            ?? throw new InjectorException(
                $"在 {HostMethodName} 中找不到锚点 {AnchorTypeName}.{AnchorMethodName}(...)，游戏的 Godot 入口可能已经变化。");

        if (anchor.Next is not { } next)
        {
            throw new InjectorException("锚点之后没有后续指令，入口方法结构异常。");
        }

        ILProcessor il = body.GetILProcessor();
        Instruction call = il.Create(OpCodes.Call, bootstrap);
        Instruction leaveTry = il.Create(OpCodes.Leave, next);
        Instruction pop = il.Create(OpCodes.Pop);
        Instruction leaveHandler = il.Create(OpCodes.Leave, next);

        il.InsertAfter(anchor, call);
        il.InsertAfter(call, leaveTry);
        il.InsertAfter(leaveTry, pop);
        il.InsertAfter(pop, leaveHandler);

        // 嵌套的（内层）异常处理块必须排在外层之前
        body.ExceptionHandlers.Insert(0, new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = call,
            TryEnd = pop,
            HandlerStart = pop,
            HandlerEnd = next,
            CatchType = module.TypeSystem.Object,
        });

        body.OptimizeMacros();
    }

    /// <summary>
    /// 引导代码用到的外部引用。全部指向游戏程序集已经引用的门面程序集（System.Runtime 等），
    /// 而不是 Injector 自身运行时的 System.Private.CoreLib，与 C# 编译器生成的引用方式一致。
    /// </summary>
    private sealed class References
    {
        private readonly ModuleDefinition _module;
        private readonly AssemblyNameReference _systemRuntime;

        public References(ModuleDefinition module)
        {
            _module = module;
            _systemRuntime = Find("System.Runtime")
                ?? throw new InjectorException("游戏程序集没有引用 System.Runtime，无法生成引导代码。");
            AssemblyNameReference godotSharp = Find("GodotSharp")
                ?? throw new InjectorException("游戏程序集没有引用 GodotSharp，无法生成引导代码。");
            AssemblyNameReference loader = GetOrAdd("System.Runtime.Loader");

            TypeSystem ts = module.TypeSystem;
            TypeReference type = Runtime("System", "Type");
            TypeReference runtimeTypeHandle = Runtime("System", "RuntimeTypeHandle", isValueType: true);
            TypeReference methodInfo = Runtime("System.Reflection", "MethodInfo");
            TypeReference methodBase = Runtime("System.Reflection", "MethodBase");
            TypeReference path = Runtime("System.IO", "Path");
            TypeReference file = Runtime("System.IO", "File");
            TypeReference loadContext = new("System.Runtime.Loader", "AssemblyLoadContext", module, loader);
            TypeReference gd = new("Godot", "GD", module, godotSharp);

            Assembly = Runtime("System.Reflection", "Assembly");
            Exception = Runtime("System", "Exception");

            GetTypeFromHandle = Static(type, "GetTypeFromHandle", type, runtimeTypeHandle);
            TypeGetAssembly = Instance(type, "get_Assembly", Assembly);
            AssemblyGetLocation = Instance(Assembly, "get_Location", ts.String);
            PathGetDirectoryName = Static(path, "GetDirectoryName", ts.String, ts.String);
            PathCombine3 = Static(path, "Combine", ts.String, ts.String, ts.String, ts.String);
            FileExists = Static(file, "Exists", ts.Boolean, ts.String);
            GetLoadContext = Static(loadContext, "GetLoadContext", loadContext, Assembly);
            LoadFromAssemblyPath = Instance(loadContext, "LoadFromAssemblyPath", Assembly, ts.String);
            AssemblyGetType = Instance(Assembly, "GetType", type, ts.String, ts.Boolean);
            TypeGetMethod = Instance(type, "GetMethod", methodInfo, ts.String);
            MethodBaseInvoke = Instance(methodBase, "Invoke", ts.Object, ts.Object, new ArrayType(ts.Object));
            StringConcat2 = Static(ts.String, "Concat", ts.String, ts.String, ts.String);
            ObjectToString = Instance(ts.Object, "ToString", ts.String);
            GdPrint = Static(gd, "Print", ts.Void, ts.String);
            GdPrintErr = Static(gd, "PrintErr", ts.Void, ts.String);
        }

        public TypeReference Assembly { get; }
        public TypeReference Exception { get; }
        public MethodReference GetTypeFromHandle { get; }
        public MethodReference TypeGetAssembly { get; }
        public MethodReference AssemblyGetLocation { get; }
        public MethodReference PathGetDirectoryName { get; }
        public MethodReference PathCombine3 { get; }
        public MethodReference FileExists { get; }
        public MethodReference GetLoadContext { get; }
        public MethodReference LoadFromAssemblyPath { get; }
        public MethodReference AssemblyGetType { get; }
        public MethodReference TypeGetMethod { get; }
        public MethodReference MethodBaseInvoke { get; }
        public MethodReference StringConcat2 { get; }
        public MethodReference ObjectToString { get; }
        public MethodReference GdPrint { get; }
        public MethodReference GdPrintErr { get; }

        private AssemblyNameReference? Find(string name) =>
            _module.AssemblyReferences.FirstOrDefault(a => a.Name == name);

        private AssemblyNameReference GetOrAdd(string name)
        {
            if (Find(name) is { } existing)
            {
                return existing;
            }

            // 框架门面程序集与 System.Runtime 使用相同的版本号和公钥标记
            var created = new AssemblyNameReference(name, _systemRuntime.Version)
            {
                PublicKeyToken = _systemRuntime.PublicKeyToken,
            };
            _module.AssemblyReferences.Add(created);
            return created;
        }

        private TypeReference Runtime(string ns, string name, bool isValueType = false) =>
            new(ns, name, _module, _systemRuntime, isValueType);

        private static MethodReference Static(TypeReference declaringType, string name, TypeReference returnType, params TypeReference[] parameters) =>
            Create(declaringType, name, returnType, hasThis: false, parameters);

        private static MethodReference Instance(TypeReference declaringType, string name, TypeReference returnType, params TypeReference[] parameters) =>
            Create(declaringType, name, returnType, hasThis: true, parameters);

        private static MethodReference Create(TypeReference declaringType, string name, TypeReference returnType, bool hasThis, TypeReference[] parameters)
        {
            var method = new MethodReference(name, returnType, declaringType) { HasThis = hasThis };
            foreach (TypeReference parameter in parameters)
            {
                method.Parameters.Add(new ParameterDefinition(parameter));
            }

            return method;
        }
    }
}
