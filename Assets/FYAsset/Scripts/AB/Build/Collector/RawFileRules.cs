using System;
using System.Collections.Generic;

/// <summary>
/// 项目级 RawFile 规则。Patterns 使用与 Ignore 相同的 gitignore 基础语义；
/// 最后一个匹配规则决定文件是否按原始文件构建与加载。
/// </summary>
[Serializable]
public class RawFileRules
{
    /// <summary>RawFile 匹配规则列表；正向规则启用，! 规则取消前面的匹配。</summary>
    public List<string> Patterns = new();

    public bool Matches(string assetPath)
    {
        return GitIgnoreMatcher.Evaluate(assetPath, Patterns);
    }
}
