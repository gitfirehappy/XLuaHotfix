using System.Collections.Generic;

/// <summary>
/// TaskVerifyBuildResult 的 6 项输出完整性校验结果。
/// TaskOrganizeOutput 消费此结果生成构建摘要。
/// </summary>
public class BuildVerificationResult
{
    public bool Success;
    public List<VerificationIssue> Issues = new();

    /// <summary>Error 数量（构造时写入，避免重复遍历）</summary>
    public int ErrorCount;

    /// <summary>Warning 数量（构造时写入，避免重复遍历）</summary>
    public int WarningCount;
}

public class VerificationIssue
{
    /// <summary>检查项标识（如 "FILE_EXISTENCE" / "HASH_RE_VERIFY"）</summary>
    public string CheckName;

    /// <summary>严重级别：Error（中止构建）或 Warning（继续但记录）</summary>
    public IssueLevel Level;

    /// <summary>关联的 Bundle 名称（全局检查可为 null）</summary>
    public string BundleName;

    /// <summary>人类可读描述</summary>
    public string Message;
}

public enum IssueLevel
{
    Error,
    Warning
}
