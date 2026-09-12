using System;
using System.Collections.Generic;

/// <summary>
/// 依赖共享策略。决策逻辑在 Editor 程序集的 DependencyAnalyzer 中执行。
/// </summary>
/// <remarks>
/// 默认口径：单引用隐式依赖随引用方打包，多引用隐式依赖提取共享内容。
/// ForceShare 强制提取共享内容；NoShare 禁止提取，命中多引用依赖时无法同时满足唯一物理归属，
/// 与“同时命中 ForceShare 和 NoShare”一样判为配置错误（SHAREPOLICY_CONFLICT），构建阻断。
/// </remarks>
[Serializable]
public class SharePolicyConfig
{
    /// <summary>强制共享的项目相对路径匹配规则。</summary>
    public List<string> ForceSharePatterns = new();

    /// <summary>禁止共享的项目相对路径匹配规则。</summary>
    public List<string> NoSharePatterns = new();
}
