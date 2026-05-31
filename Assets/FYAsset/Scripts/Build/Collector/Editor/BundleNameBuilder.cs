/// <summary>
/// Bundle logical-name builder for Group BundlePackingMode.
/// Output does not include content hash or file extension; build tasks append those when needed.
/// </summary>
public static class BundleNameBuilder
{
    #region Constants

    private const int ShortGuidLength = 8;

    #endregion

    #region Public Methods

    /// <summary>
    /// Validate PackageName / GroupName / Label characters. Returns null when valid.
    /// </summary>
    public static string ValidateSegment(string segment)
    {
        return ValidateAgainst(segment, SystemIdentifiers.ReservedChars);
    }

    /// <summary>
    /// Validate BundleKey characters. Allows "~" because it is the intentional key joiner.
    /// </summary>
    public static string ValidateBundleKey(string bundleKey)
    {
        if (string.Equals(bundleKey, SystemIdentifiers.UnlabeledBundleKey, System.StringComparison.Ordinal))
            return null;

        return ValidateAgainst(bundleKey, SystemIdentifiers.BundleKeyReservedChars);
    }

    public static string Build(
        string packageName,
        string groupName,
        BundlePackingMode mode,
        string address,
        string assetGuid,
        System.Collections.Generic.List<string> finalLabels)
    {
        string safePkg = SanitizeSegment(packageName);
        string safeGroup = SanitizeSegment(groupName);
        string modeSegment = GetModeSegment(mode);
        string bundleKey = GetBundleKey(mode, address, assetGuid, finalLabels);

        if (mode == BundlePackingMode.PackTogether)
        {
            return string.Concat(
                safePkg,
                SystemIdentifiers.SegmentSeparator,
                safeGroup,
                SystemIdentifiers.SegmentSeparator,
                modeSegment);
        }

        return string.Concat(
            safePkg,
            SystemIdentifiers.SegmentSeparator,
            safeGroup,
            SystemIdentifiers.SegmentSeparator,
            modeSegment,
            SystemIdentifiers.SegmentSeparator,
            SanitizeBundleKey(bundleKey));
    }

    public static string BuildShared(string packageName, string bundleKey)
    {
        return string.Concat(
            SanitizeSegment(packageName),
            SystemIdentifiers.SegmentSeparator,
            "$shared",
            SystemIdentifiers.SegmentSeparator,
            SanitizeBundleKey(bundleKey));
    }

    public static string GetBundleKey(
        BundlePackingMode mode,
        string address,
        string assetGuid,
        System.Collections.Generic.List<string> finalLabels)
    {
        switch (mode)
        {
            case BundlePackingMode.PackTogether:
                return "all";
            case BundlePackingMode.PackSeparately:
                return string.Concat(NormalizeAddressKey(address), SystemIdentifiers.LabelSeparator, ShortGuid(assetGuid));
            case BundlePackingMode.PackTogetherByLabel:
                return BuildLabelKey(finalLabels);
            default:
                return SystemIdentifiers.DefaultBundleKey;
        }
    }

    #endregion

    #region Private Methods

    private static string ValidateAgainst(string value, char[] blacklist)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            for (int j = 0; j < blacklist.Length; j++)
            {
                if (c == blacklist[j])
                    return string.Concat("'", value, "' contains reserved character '", c, "' at position ", i.ToString());
            }
        }

        return null;
    }

    private static string SanitizeSegment(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return SystemIdentifiers.DefaultBundleKey;

        string lowered = raw.ToLowerInvariant();
        return lowered.Length > 0 ? lowered : SystemIdentifiers.DefaultBundleKey;
    }

    private static string SanitizeBundleKey(string raw)
    {
        if (string.Equals(raw, SystemIdentifiers.UnlabeledBundleKey, System.StringComparison.Ordinal))
            return raw;

        return SanitizeSegment(raw);
    }

    private static string NormalizeAddressKey(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return SystemIdentifiers.DefaultBundleKey;

        string lowered = raw.ToLowerInvariant();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(lowered.Length);
        bool lastWasSeparator = false;
        for (int i = 0; i < lowered.Length; i++)
        {
            char c = lowered[i];
            if (IsBundleKeyCharAllowed(c))
            {
                builder.Append(c);
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        while (builder.Length > 0 && builder[builder.Length - 1] == '-')
            builder.Length--;

        return builder.Length > 0 ? builder.ToString() : SystemIdentifiers.DefaultBundleKey;
    }

    private static bool IsBundleKeyCharAllowed(char c)
    {
        for (int i = 0; i < SystemIdentifiers.BundleKeyReservedChars.Length; i++)
        {
            if (c == SystemIdentifiers.BundleKeyReservedChars[i])
                return false;
        }

        return c != SystemIdentifiers.LabelSeparator;
    }

    private static string GetModeSegment(BundlePackingMode mode)
    {
        switch (mode)
        {
            case BundlePackingMode.PackTogether:
                return "all";
            case BundlePackingMode.PackSeparately:
                return "asset";
            case BundlePackingMode.PackTogetherByLabel:
                return "labels";
            default:
                return "unknown";
        }
    }

    private static string BuildLabelKey(System.Collections.Generic.List<string> labels)
    {
        if (labels == null || labels.Count == 0)
            return SystemIdentifiers.UnlabeledBundleKey;

        System.Collections.Generic.List<string> sorted = new System.Collections.Generic.List<string>();
        for (int i = 0; i < labels.Count; i++)
        {
            if (!string.IsNullOrEmpty(labels[i]))
                sorted.Add(labels[i].ToLowerInvariant());
        }

        if (sorted.Count == 0)
            return SystemIdentifiers.UnlabeledBundleKey;

        sorted.Sort(System.StringComparer.Ordinal);
        return string.Join(SystemIdentifiers.LabelSeparator.ToString(), sorted);
    }

    private static string ShortGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return "noguid00";

        return guid.Length <= ShortGuidLength
            ? guid.ToLowerInvariant()
            : guid.Substring(0, ShortGuidLength).ToLowerInvariant();
    }

    #endregion
}
