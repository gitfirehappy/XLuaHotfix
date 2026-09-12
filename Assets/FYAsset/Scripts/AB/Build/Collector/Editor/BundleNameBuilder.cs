/// <summary>
/// 面向 Group BundlePackingMode 的 Bundle 逻辑名构建器。
/// 输出不包含内容 hash 或文件扩展名；构建 Task 在需要时追加。
/// </summary>
public static class BundleNameBuilder
{
    private const int ShortGuidLength = 8;

    /// <summary>物理内容文件名使用的身份哈希长度；改变它属于命名规则变更。</summary>
    private const int PhysicalNameHashLength = 12;

    /// <summary>命名规则版本；参与身份哈希与构建缓存指纹，规则变化必须同时提升。</summary>
    public const int PhysicalNameRuleVersion = 2;

    /// <summary>可读段最大长度；超长截断，唯一性由 12 位身份哈希承担。</summary>
    private const int MaxReadableNameLength = 60;

    /// <summary>
    /// 由逻辑内容名与可读段派生物理文件名：{group}_{kind}[_{readable}]_{identityHash12}。
    /// </summary>
    /// <remarks>
    /// 逻辑内容名承载分组、打包模式与打包键（含 Address），长度可达上百字符；
    /// 部署路径较深时（Windows MAX_PATH=260）运行时按完整路径读取会失败，
    /// 因此物理名只保留分组、打包模式可读段与 12 位身份哈希，映射关系由 Manifest 的 ContentEntry.FileName 承担。
    /// 自动 Address 即使是完整路径也必须传入资源短名作为可读段；整组与标签模式由打包模式段决定可读段。
    /// </remarks>
    public static string BuildPhysicalName(string contentName, string readableName)
    {
        if (string.IsNullOrEmpty(contentName))
            return SystemIdentifiers.DefaultBundleKey;

        string[] segments = contentName.Split(SystemIdentifiers.SegmentSeparator);
        string group = SanitizePhysicalSegment(segments.Length > 0 ? segments[0] : null);
        string contentTypeSegment = segments.Length > 1 ? segments[1] : string.Empty;
        string modeSegment = segments.Length > 3 ? segments[3] : string.Empty;
        string keySegment = segments.Length > 4 ? segments[4] : string.Empty;

        string kind = ResolvePhysicalKind(contentTypeSegment, modeSegment, keySegment);
        string readable = ResolveReadableSegment(kind, keySegment, readableName);

        string identity = string.Concat(
            "v", PhysicalNameRuleVersion.ToString(),
            "|", contentName,
            "|", kind,
            "|", readable);
        string hash = HashGenerator.GenerateStringHash(identity);
        string hash12 = string.IsNullOrEmpty(hash)
            ? "000000000000"
            : hash.Length <= PhysicalNameHashLength
                ? hash.ToLowerInvariant()
                : hash.Substring(0, PhysicalNameHashLength).ToLowerInvariant();

        if (string.IsNullOrEmpty(group))
            group = SystemIdentifiers.DefaultBundleKey;

        return string.IsNullOrEmpty(readable)
            ? string.Concat(group, SystemIdentifiers.SegmentSeparator, kind,
                SystemIdentifiers.SegmentSeparator, hash12)
            : string.Concat(group, SystemIdentifiers.SegmentSeparator, kind,
                SystemIdentifiers.SegmentSeparator, readable, SystemIdentifiers.SegmentSeparator, hash12);
    }

    /// <summary>没有显式可读段时回退到内容名的打包键段。</summary>
    public static string BuildPhysicalName(string contentName) => BuildPhysicalName(contentName, null);

    private static string ResolvePhysicalKind(string contentTypeSegment, string modeSegment, string keySegment)
    {
        if (string.Equals(contentTypeSegment, "scene", System.StringComparison.OrdinalIgnoreCase))
            return "scene";
        if (string.Equals(contentTypeSegment, "rawfile", System.StringComparison.OrdinalIgnoreCase))
            return "raw";
        if (string.Equals(modeSegment, "all", System.StringComparison.OrdinalIgnoreCase))
            return "all";
        if (string.Equals(modeSegment, "labels", System.StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(keySegment, SystemIdentifiers.UnlabeledBundleKey, System.StringComparison.Ordinal)
                ? "unlabeled"
                : "labels";
        }

        return "asset";
    }

    private static string ResolveReadableSegment(string kind, string keySegment, string readableName)
    {
        switch (kind)
        {
            case "all":
            case "unlabeled":
                return string.Empty;
            case "labels":
                return SanitizePhysicalSegment(keySegment);
            default:
                string readable = SanitizePhysicalSegment(readableName);
                return string.IsNullOrEmpty(readable) ? SanitizePhysicalSegment(keySegment) : readable;
        }
    }

    /// <summary>可读段只允许跨平台安全字符；保留大小写与原有文本，超长截断。</summary>
    private static string SanitizePhysicalSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new System.Text.StringBuilder(value.Length);
        bool lastWasDash = false;
        for (int i = 0; i < value.Length && builder.Length < MaxReadableNameLength; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
            {
                builder.Append(c);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        while (builder.Length > 0 && (builder[builder.Length - 1] == '-' || builder[builder.Length - 1] == '.'))
            builder.Length--;
        return builder.ToString();
    }

    /// <summary>
    /// 校验 GroupName / Label 字符；合法时返回 null。
    /// </summary>
    public static string ValidateSegment(string segment)
    {
        return ValidateAgainst(segment, SystemIdentifiers.ReservedChars);
    }

    /// <summary>
    /// 校验 BundleKey 字符；允许 "~"，因为它是有意使用的 key 连接符。
    /// </summary>
    public static string ValidateBundleKey(string bundleKey)
    {
        if (string.Equals(bundleKey, SystemIdentifiers.UnlabeledBundleKey, System.StringComparison.Ordinal))
            return null;

        return ValidateAgainst(bundleKey, SystemIdentifiers.BundleKeyReservedChars);
    }

    /// <summary>
    /// 组装显式资源的内容逻辑名：分组 / 内容类型 / 主类型 / 打包模式 / 打包键。
    /// </summary>
    public static string Build(
        string groupName,
        BundlePackingMode mode,
        string address,
        string assetGuid,
        System.Collections.Generic.List<string> finalLabels,
        AssetContentType contentType,
        string primaryType)
    {
        string safeGroup = SanitizeSegment(groupName);
        string contentTypeSegment = GetContentTypeSegment(contentType);
        string typeSegment = SanitizeSegment(primaryType);
        string modeSegment = GetModeSegment(mode);
        string bundleKey = GetBundleKey(mode, address, assetGuid, finalLabels);

        return string.Concat(
            safeGroup,
            SystemIdentifiers.SegmentSeparator,
            contentTypeSegment,
            SystemIdentifiers.SegmentSeparator,
            typeSegment,
            SystemIdentifiers.SegmentSeparator,
            modeSegment,
            SystemIdentifiers.SegmentSeparator,
            SanitizeBundleKey(bundleKey));
    }

    public static string BuildShared(string bundleKey)
    {
        return string.Concat(
            SystemIdentifiers.SharedGroupName,
            SystemIdentifiers.SegmentSeparator,
            SanitizeBundleKey(bundleKey));
    }

    public static string BuildShared(
        string bundleKey,
        AssetContentType contentType,
        string primaryType)
    {
        return string.Concat(
            SystemIdentifiers.SharedGroupName,
            SystemIdentifiers.SegmentSeparator,
            GetContentTypeSegment(contentType),
            SystemIdentifiers.SegmentSeparator,
            SanitizeSegment(primaryType),
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

    /// <summary>内容类型段。段名属于既有 Bundle 逻辑名规则，改名会改动全部已产出内容的名称。</summary>
    private static string GetContentTypeSegment(AssetContentType contentType)
    {
        switch (contentType)
        {
            case AssetContentType.SerializedObject:
                return "serialized";
            case AssetContentType.RawFile:
                return "rawfile";
            case AssetContentType.Scene:
                return "scene";
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
}
