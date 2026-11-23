using System.Reflection;
using System.Text;
using Melvor2Patch.Core;

namespace Melvor2Patch.Diagnostics;

/// <summary>
/// 运行时元数据导出：按真实名称、真实可访问性导出游戏程序集的全部类型与成员（含编译器生成和嵌套类型），
/// 用于对照反编译源码确认补丁目标。
/// <para>
/// 默认关闭。在 mod 目录放一个空文件 <c>dump-metadata.flag</c> 即可在下次启动时导出到
/// <c>Melvor2Patch\dump\GamesByMalcs.Melvor2.txt</c>（后台线程执行，不阻塞启动）。
/// </para>
/// </summary>
internal static class MetadataDumper
{
    public const string FlagFileName = "dump-metadata.flag";

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void RunIfRequested()
    {
        if (!File.Exists(ModContext.GetPath(FlagFileName)))
        {
            return;
        }

        Assembly assembly = ModContext.GameAssembly;
        string output = ModContext.GetPath("dump", assembly.GetName().Name + ".txt");
        ModLog.Info($"检测到 {FlagFileName}，开始导出元数据 -> {output}");

        Task.Run(() =>
        {
            try
            {
                int count = Dump(assembly, output);
                ModLog.Info($"元数据导出完成：{count} 个类型。");
            }
            catch (Exception ex)
            {
                ModLog.Error("元数据导出失败。", ex);
            }
        });
    }

    public static int Dump(Assembly assembly, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>().ToArray();
        }

        using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine($"// {assembly.FullName}");
        writer.WriteLine($"// 导出时间 {DateTime.Now:yyyy-MM-dd HH:mm:ss}，共 {types.Length} 个类型");
        writer.WriteLine("// [外部不可见] 表示声明为 public，但因外层类型非 public 而无法从程序集外直接引用");

        foreach (Type type in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            writer.WriteLine(DescribeType(type));
            WriteMembers(writer, type);
        }

        return types.Length;
    }

    private static void WriteMembers(StreamWriter writer, Type type)
    {
        foreach (FieldInfo field in Safe(() => type.GetFields(Declared)))
        {
            writer.WriteLine(Safe(() => $"    {Access(field)}{(field.IsStatic ? "static " : "")}{(field.IsInitOnly ? "readonly " : "")}field {TypeName(field.FieldType)} {field.Name}"));
        }

        foreach (PropertyInfo property in Safe(() => type.GetProperties(Declared)))
        {
            writer.WriteLine(Safe(() =>
            {
                string accessors = string.Join(" ", new[] { property.GetMethod, property.SetMethod }
                    .OfType<MethodInfo>()
                    .Select(m => $"{Access(m)}{(m == property.GetMethod ? "get" : "set")};"));
                return $"    property {TypeName(property.PropertyType)} {property.Name} {{ {accessors} }}";
            }));
        }

        foreach (EventInfo @event in Safe(() => type.GetEvents(Declared)))
        {
            writer.WriteLine(Safe(() => $"    event {TypeName(@event.EventHandlerType)} {@event.Name}"));
        }

        foreach (ConstructorInfo constructor in Safe(() => type.GetConstructors(Declared)))
        {
            writer.WriteLine(Safe(() => $"    {Access(constructor)}{(constructor.IsStatic ? "static " : "")}{constructor.Name}({Parameters(constructor)})"));
        }

        foreach (MethodInfo method in Safe(() => type.GetMethods(Declared)))
        {
            writer.WriteLine(Safe(() =>
            {
                string generic = method.IsGenericMethodDefinition
                    ? $"<{string.Join(", ", method.GetGenericArguments().Select(a => a.Name))}>"
                    : "";
                return $"    {Access(method)}{Modifiers(method)}{TypeName(method.ReturnType)} {method.Name}{generic}({Parameters(method)})";
            }));
        }
    }

    private static string DescribeType(Type type)
    {
        string access = type switch
        {
            { IsNested: false } => type.IsPublic ? "public" : "internal",
            { IsNestedPublic: true } => "public",
            { IsNestedPrivate: true } => "private",
            { IsNestedFamily: true } => "protected",
            { IsNestedAssembly: true } => "internal",
            { IsNestedFamORAssem: true } => "protected internal",
            _ => "private protected",
        };

        string kind = type switch
        {
            { IsInterface: true } => "interface",
            { IsEnum: true } => "enum",
            { IsValueType: true } => "struct",
            _ when typeof(Delegate).IsAssignableFrom(type.BaseType) => "delegate",
            { IsAbstract: true, IsSealed: true } => "static class",
            { IsAbstract: true } => "abstract class",
            { IsSealed: true } => "sealed class",
            _ => "class",
        };

        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum))
        {
            bases.Add(TypeName(baseType));
        }

        bases.AddRange(Safe(() => type.GetInterfaces()).Select(TypeName));
        string inheritance = bases.Count > 0 ? " : " + string.Join(", ", bases) : "";
        string hidden = access.StartsWith("public", StringComparison.Ordinal) && !type.IsVisible ? " [外部不可见]" : "";

        return $"{access} {kind} {type.FullName}{inheritance}{hidden}";
    }

    private static string Access(MethodBase method) => method switch
    {
        { IsPublic: true } => "public ",
        { IsPrivate: true } => "private ",
        { IsFamily: true } => "protected ",
        { IsAssembly: true } => "internal ",
        { IsFamilyOrAssembly: true } => "protected internal ",
        _ => "private protected ",
    };

    private static string Access(FieldInfo field) => field switch
    {
        { IsPublic: true } => "public ",
        { IsPrivate: true } => "private ",
        { IsFamily: true } => "protected ",
        { IsAssembly: true } => "internal ",
        { IsFamilyOrAssembly: true } => "protected internal ",
        _ => "private protected ",
    };

    private static string Modifiers(MethodInfo method)
    {
        if (method.IsStatic)
        {
            return "static ";
        }

        if (method.IsAbstract)
        {
            return "abstract ";
        }

        if (!method.IsVirtual || method.IsFinal)
        {
            return "";
        }

        return method.GetBaseDefinition().DeclaringType == method.DeclaringType ? "virtual " : "override ";
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(p => $"{TypeName(p.ParameterType)} {p.Name}"));

    private static string TypeName(Type? type)
    {
        if (type is null)
        {
            return "?";
        }

        if (type.IsByRef)
        {
            return "ref " + TypeName(type.GetElementType());
        }

        if (type.IsArray)
        {
            return TypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsPointer)
        {
            return TypeName(type.GetElementType()) + "*";
        }

        if (type.IsGenericType)
        {
            string name = type.Name;
            int tick = name.IndexOf('`');
            if (tick >= 0)
            {
                name = name[..tick];
            }

            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
        }

        return type.Name;
    }

    private static T[] Safe<T>(Func<T[]> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string Safe(Func<string> describe)
    {
        try
        {
            return describe();
        }
        catch (Exception ex)
        {
            return $"    <无法描述：{ex.GetType().Name}: {ex.Message}>";
        }
    }
}
