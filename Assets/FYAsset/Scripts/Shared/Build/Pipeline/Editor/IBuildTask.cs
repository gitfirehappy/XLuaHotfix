/// <summary>
/// 构建管线 Task 的接口契约。所有 Task 必须实现此接口。
/// TaskName 作为唯一标识，配置列表顺序即执行顺序。
/// 实现类须满足：无参公共构造函数 + 独占 TaskName。
/// </summary>
public interface IBuildTask
{
    /// <summary>Task 唯一标识，如 "TaskBuildBundles"</summary>
    string TaskName { get; }

    /// <summary>执行 Task 逻辑，同步返回结果（Unity AB 构建 API 本身同步）</summary>
    BuildTaskResult Execute(BuildContext ctx);
}
