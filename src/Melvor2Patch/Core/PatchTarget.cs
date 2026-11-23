using System.Reflection;
using HarmonyLib;

namespace Melvor2Patch.Core;

/// <summary>补丁目标方法的查找辅助。</summary>
internal static class PatchTarget
{
    /// <summary>
    /// 取类型自身声明的方法（含非公开成员）。找不到时抛出异常，
    /// 由 <see cref="PatchManager"/> 记录为该补丁类应用失败，不影响其他补丁。
    /// </summary>
    public static MethodInfo Declared(Type type, string name) =>
        AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
}
