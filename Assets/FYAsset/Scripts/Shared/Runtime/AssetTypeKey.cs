using System;

/// <summary>公共运行时使用的精确资产类型键。</summary>
public static class AssetTypeKey
{
    public static string FromType(Type type)
    {
        if (type == null)
            return string.Empty;
        string assembly = type.Assembly.GetName().Name ?? string.Empty;
        string fullName = type.FullName ?? type.Name;
        return string.Concat(assembly, ":", fullName);
    }
}
