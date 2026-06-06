using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// DAG 任务调度器 —— 基于 Kahn 拓扑排序算法实现分批确定性执行和事前校验。
///
/// 两阶段模型：
///   Validate — 依赖存在性、循环依赖、Read-before-Write 警告
///   Execute — 入度表驱动批循环，批内字母序确定性执行（单线程串行），Fatal 中止传播
///
/// WriteKeys 表示 Task 会写入或更新 BuildContext Key，不表示独占写锁。
/// </summary>
public static class DAGScheduler
{
    #region Public API

    /// <summary>执行构建管线：先校验，后按拓扑序分批执行全部 Task</summary>
    public static BuildResult Execute(BuildPipelineConfig config, BuildContext context)
    {
        return Execute(config, context, null);
    }

    /// <summary>执行构建管线，并通过 options 上报每个 Task 的可视状态</summary>
    public static BuildResult Execute(BuildPipelineConfig config, BuildContext context, BuildExecutionOptions options)
    {
        return Execute(config, context, options, null, null);
    }

    /// <summary>执行构建管线，并可在指定 Task 完成后停止。用于 Diff Preview 等只读预览流程。</summary>
    public static BuildResult Execute(BuildPipelineConfig config, BuildContext context, BuildExecutionOptions options, string stopAfterTaskName)
    {
        return Execute(config, context, options, stopAfterTaskName, null);
    }

    /// <summary>执行构建管线，并可通过 whitelist 限制允许执行的 Task 集合。</summary>
    public static BuildResult Execute(BuildPipelineConfig config, BuildContext context, BuildExecutionOptions options, string stopAfterTaskName, HashSet<string> taskWhitelist)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (context == null) throw new ArgumentNullException(nameof(context));

        var validation = ValidateInternal(config, taskWhitelist);
        if (!validation.Success)
            return validation;

        return ExecuteInternal(config, context, options, stopAfterTaskName, taskWhitelist);
    }

    /// <summary>仅运行校验检查，不执行 Task</summary>
    public static BuildResult Validate(BuildPipelineConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        return ValidateInternal(config);
    }

    #endregion

    #region Validation

    private static BuildResult ValidateInternal(BuildPipelineConfig config, HashSet<string> taskWhitelist = null)
    {
        var errors = new List<BuildTaskResult>();
        var warnings = new List<BuildTaskResult>();

        var enabled = config.Tasks
            .Where(e => e.Enabled && (taskWhitelist == null || taskWhitelist.Contains(e.TaskName)))
            .ToList();
        if (enabled.Count == 0)
            return ErrorResult(config, new List<BuildTaskResult> { BuildTaskResult.Fail(
                "NO_ENABLED_TASKS", "管线配置中无已启用的 Task。", true) });

        if (taskWhitelist == null)
        {
            List<string> missingBackboneTasks = BuildPipelineBackbone.GetMissingRequiredTasks(config);
            if (missingBackboneTasks.Count > 0)
            {
                errors.Add(BuildTaskResult.Fail(
                    "MISSING_BACKBONE_TASK",
                    $"管线配置缺少必需主干 Task: {string.Join(", ", missingBackboneTasks)}。请更新 BuildPipelineConfig asset。",
                    true));
                return ErrorResult(config, errors);
            }
        }

        // 解析所有 Enabled Task
        var instances = new Dictionary<string, IBuildTask>(StringComparer.Ordinal);
        var enabledNames = new HashSet<string>(enabled.Select(e => e.TaskName), StringComparer.Ordinal);
        foreach (var entry in enabled)
        {
            if (!BuildTaskResolver.Exists(entry.TaskName))
            {
                errors.Add(BuildTaskResult.Fail(
                    "TASK_NOT_FOUND",
                    $"'{entry.TaskName}' — 未找到对应的 IBuildTask 实现。", true));
                continue;
            }
            instances[entry.TaskName] = BuildTaskResolver.CreateTask(entry.TaskName);
        }
        if (errors.Count > 0) return ErrorResult(config, errors);

        // 校验 1：所有 DependsOn（合并 IBuildTask + SO TaskEntry）指向已启用的 TaskName
        // 只允许依赖 enabled task，防止依赖 disabled task 被静默忽略导致错误拓扑
        foreach (var instance in instances.Values)
        {
            var taskDeps = GetMergedDependencies(instance, config);
            if (taskDeps.Length == 0) continue;
            foreach (var dep in taskDeps)
            {
                if (!enabledNames.Contains(dep))
                {
                    string reason = config.Tasks.Any(e => e.TaskName == dep && !e.Enabled)
                        ? $"'{dep}' 已禁用 — 请启用该 Task 或更新 '{instance.TaskName}' 的依赖。"
                        : $"'{dep}' — 不在 Task 列表中。";
                    errors.Add(BuildTaskResult.Fail(
                        "MISSING_DEPENDENCY",
                        $"'{instance.TaskName}' depends on '{dep}': {reason}", true));
                }
            }
        }
        if (errors.Count > 0) return ErrorResult(config, errors);

        // 构建邻接表
        BuildAdjacency(instances, config, out var indegree, out var successors);

        // 校验 2：Kahn 拓扑排序 → 检测循环依赖
        var sorted = TopologicalSort(instances.Keys.ToList(), indegree, successors);
        if (sorted.Count < instances.Count)
        {
            var cyclic = instances.Keys.Except(sorted).ToList();
            errors.Add(BuildTaskResult.Fail("CIRCULAR_TASK_DEPENDENCY",
                $"检测到循环依赖: {string.Join(", ", cyclic)}。", true));
            return ErrorResult(config, errors);
        }

        // 校验 3：Read-before-Write 警告
        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var taskName in sorted)
        {
            var instance = instances[taskName];
            if (instance.ReadKeys != null)
            {
                foreach (var key in instance.ReadKeys)
                {
                    bool selfProduce = instance.WriteKeys != null && instance.WriteKeys.Contains(key);
                    if (!selfProduce && !produced.Contains(key))
                    {
                        warnings.Add(BuildTaskResult.Fail("UNSATISFIED_READ_KEY",
                            $"'{taskName}' 读取 '{key}'，但没有前置 Task 产出该 Key。", false));
                    }
                }
            }
            if (instance.WriteKeys != null)
            {
                foreach (var key in instance.WriteKeys)
                    produced.Add(key);
            }
        }

        var result = new BuildResult { Success = true, TotalTasks = instances.Count };
        result.TaskResults.AddRange(warnings);
        return result;
    }

    private static List<string> TopologicalSort(
        List<string> nodes,
        Dictionary<string, int> indegree,
        Dictionary<string, List<string>> successors)
    {
        var sorted = new List<string>();
        var inDeg = new Dictionary<string, int>(indegree, StringComparer.Ordinal);
        var queue = new Queue<string>(nodes.Where(n => inDeg[n] == 0).OrderBy(n => n, StringComparer.Ordinal));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(current);
            foreach (var succ in successors[current])
            {
                inDeg[succ]--;
                if (inDeg[succ] == 0)
                    queue.Enqueue(succ);
            }
        }
        return sorted;
    }

    #endregion

    #region Execution

    private static BuildResult ExecuteInternal(BuildPipelineConfig config, BuildContext context, BuildExecutionOptions options, string stopAfterTaskName, HashSet<string> taskWhitelist)
    {
        var enabled = config.Tasks
            .Where(e => e.Enabled && (taskWhitelist == null || taskWhitelist.Contains(e.TaskName)))
            .ToList();
        var instances = new Dictionary<string, IBuildTask>(StringComparer.Ordinal);
        foreach (var entry in enabled)
            instances[entry.TaskName] = BuildTaskResolver.CreateTask(entry.TaskName);
        foreach (var taskName in instances.Keys)
            options?.Report(taskName, BuildTaskExecutionStatus.Pending);

        // 构建邻接表（复用 ValidateInternal 同款逻辑）
        BuildAdjacency(instances, config, out var indegree, out var successors);

        var results = new List<BuildTaskResult>();
        var executed = new HashSet<string>(StringComparer.Ordinal);
        var remaining = new HashSet<string>(instances.Keys, StringComparer.Ordinal);
        var fatalAbort = false;

        while (remaining.Count > 0 && !fatalAbort)
        {
            var ready = remaining
                .Where(n => indegree[n] == 0)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0)
            {
                results.Add(BuildTaskResult.Fail("SCHEDULER_DEADLOCK",
                    $"存在无法满足的依赖，剩余未执行 Task: {string.Join(", ", remaining)}。", true));
                break;
            }

            for (int i = 0; i < ready.Count && !fatalAbort; i++)
            {
                var taskName = ready[i];
                indegree[taskName] = -1;
                remaining.Remove(taskName);
                executed.Add(taskName);

                var task = instances[taskName];
                BuildTaskResult taskResult;
                options?.Report(taskName, BuildTaskExecutionStatus.Running);
                try
                {
                    taskResult = task.Execute(context) ?? BuildTaskResult.Fail(
                        "NULL_RESULT", $"'{taskName}' 返回了 null。", true);
                }
                catch (Exception ex)
                {
                    taskResult = BuildTaskResult.Fail("TASK_EXECUTION_ERROR",
                        $"'{taskName}' 执行异常 — {ex.GetType().Name}: {ex.Message}。", true);
                }

                results.Add(taskResult);
                options?.Report(
                    taskName,
                    taskResult.Success ? BuildTaskExecutionStatus.Success : BuildTaskExecutionStatus.Failed,
                    taskResult);

                if (taskResult.IsFatal && !taskResult.Success)
                {
                    fatalAbort = true;
                    break;
                }

                if (!string.IsNullOrEmpty(stopAfterTaskName)
                    && string.Equals(taskName, stopAfterTaskName, StringComparison.Ordinal))
                {
                    remaining.Clear();
                    break;
                }

                foreach (var succ in successors[taskName])
                {
                    if (indegree.TryGetValue(succ, out int deg) && deg > 0)
                        indegree[succ] = deg - 1;
                }
            }
        }

        // 标记因 Fatal 中止而跳过的 Task
        foreach (var taskName in remaining)
            options?.Report(taskName, BuildTaskExecutionStatus.Skipped);
        var skippedTasks = instances.Count - executed.Count;

        return new BuildResult
        {
            Success = !fatalAbort && results.TrueForAll(r => r.Success),
            TotalTasks = instances.Count,
            CompletedTasks = results.Count(r => r.Success),
            SkippedTasks = skippedTasks,
            TaskResults = results
        };
    }

    #endregion

    #region Helpers

    /// <summary>根据已创建的 Task 实例构建邻接表（入度 + 后继），供 Validate 和 Execute 复用</summary>
    private static void BuildAdjacency(
        Dictionary<string, IBuildTask> instances,
        BuildPipelineConfig config,
        out Dictionary<string, int> indegree,
        out Dictionary<string, List<string>> successors)
    {
        indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        successors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var name in instances.Keys)
        {
            indegree[name] = 0;
            successors[name] = new List<string>();
        }
        foreach (var instance in instances.Values)
        {
            var taskDeps = GetMergedDependencies(instance, config);
            foreach (var dep in taskDeps)
            {
                if (instances.ContainsKey(dep))
                {
                    successors[dep].Add(instance.TaskName);
                    indegree[instance.TaskName]++;
                }
            }
        }
    }

    /// <summary>合并 IBuildTask.DependsOn 与 TaskEntry.DependsOn（SO 面板级依赖），去重</summary>
    private static string[] GetMergedDependencies(IBuildTask task, BuildPipelineConfig config)
    {
        var deps = new List<string>();
        if (task.DependsOn != null)
            deps.AddRange(task.DependsOn);

        // 读 SO 中 TaskEntry 的 DependsOn，避免配置面板依赖成为死配置
        if (config.Tasks != null)
        {
            foreach (var entry in config.Tasks)
            {
                if (entry.TaskName == task.TaskName && entry.DependsOn != null)
                {
                    foreach (var dep in entry.DependsOn)
                    {
                        if (!deps.Contains(dep))
                            deps.Add(dep);
                    }
                    break;
                }
            }
        }
        return deps.ToArray();
    }

    private static BuildResult ErrorResult(BuildPipelineConfig config, List<BuildTaskResult> errors)
    {
        return new BuildResult
        {
            Success = false,
            TotalTasks = config.Tasks.Count(e => e.Enabled),
            TaskResults = errors
        };
    }

    #endregion
}
