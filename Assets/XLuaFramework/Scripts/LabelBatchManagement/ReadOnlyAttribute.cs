using UnityEngine;

/// <summary>
/// Inspector 只读展示 attribute（替代 Unity.Collections.ReadOnly，消除对 com.unity.collections 的隐式依赖）。
/// </summary>
public sealed class ReadOnlyAttribute : PropertyAttribute
{
}
