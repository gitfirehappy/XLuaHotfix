/// <summary>
/// 构建管线 Task 的接口契约。所有 Task 必须实现此接口。
/// TaskName 作为唯一标识，DependsOn 定义拓扑依赖，ReadKeys/WriteKeys 声明 BuildContext 数据流。
/// 实现类须满足：无参公共构造函数 + 独占 TaskName。
/// </summary>
public interface IBuildTask
{
    /// <summary>Task 唯一标识，如 "TaskBuildBundles"</summary>
    string TaskName { get; }

    /// <summary>前置依赖的 TaskName 列表，DAG 调度器据此计算拓扑序</summary>
    string[] DependsOn { get; }

    /// <summary>从 BuildContext 读取的 Key 声明列表</summary>
    string[] ReadKeys { get; }

    /// <summary>向 BuildContext 写入的 Key 声明列表</summary>
    string[] WriteKeys { get; }

    /// <summary>执行 Task 逻辑，同步返回结果（Unity AB 构建 API 本身同步）</summary>
    BuildTaskResult Execute(BuildContext ctx);
}
