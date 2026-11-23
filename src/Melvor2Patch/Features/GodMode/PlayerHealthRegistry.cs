using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Components.Characters;

namespace Melvor2Patch.Features.GodMode;

/// <summary>
/// 记录玩家的全局 HP 实例（<see cref="PlayerComponent.GlobalHealth"/>）。
/// <para>
/// 全游戏只有这一份真实的玩家 HP，战斗 / 敏捷 / 偷窃各自的 <see cref="TrackableCharacterHealth"/> 都包装它。
/// <see cref="CharacterHealth"/> 类同时也被敌人和 Ward 使用，所以补丁需要按实例区分。
/// 使用弱引用表，不影响游戏对象的回收（数据编辑器等场景也会创建 Game / PlayerComponent）。
/// </para>
/// </summary>
internal static class PlayerHealthRegistry
{
    private static readonly object Marker = new();
    private static readonly ConditionalWeakTable<CharacterHealth, object> Healths = new();

    public static void Register(CharacterHealth health) => Healths.AddOrUpdate(health, Marker);

    public static bool IsPlayerHealth(CharacterHealth health) => Healths.TryGetValue(health, out _);
}
