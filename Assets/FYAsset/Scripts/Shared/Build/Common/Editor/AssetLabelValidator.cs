using System;
using System.Collections.Generic;

/// <summary>Labels契约：允许为空，已有值必须非空、无首尾空白、大小写不敏感且唯一。</summary>
public static class AssetLabelValidator
{
    public static bool TryValidate(
        IReadOnlyList<string> labels,
        out string invalidLabel,
        out string duplicateLabel)
    {
        invalidLabel = null;
        duplicateLabel = null;
        if (labels == null)
            return true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < labels.Count; i++)
        {
            string label = labels[i];
            if (string.IsNullOrEmpty(label) || !string.Equals(label, label.Trim(), StringComparison.Ordinal))
            {
                invalidLabel = label;
                return false;
            }
            if (!seen.Add(label))
            {
                duplicateLabel = label;
                return false;
            }
        }
        return true;
    }
}
