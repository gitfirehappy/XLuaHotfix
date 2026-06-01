using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// BuildGraph DAG 可视化 GraphView —— 只读，不支持用户拖线或删除节点。
/// 通过 Reload 重建全图，展示执行依赖边和数据流边。
/// </summary>
public class BuildGraphView : GraphView
{
    #region Fields & Events

    /// <summary>Task 列表或依赖发生变化时触发，通知 PipelinePanel 刷新状态栏</summary>
    public event Action OnConfigChanged;

    private BuildPipelineConfig _config;
    private readonly Dictionary<string, BuildTaskNode> _nodeMap = new(StringComparer.Ordinal);
    private bool _isBuildRunning;

    /// <summary>Reload 期间放行 graphViewChanged 事件，避免 DeleteElements 被拦截</summary>
    private bool _isReloading;

    #endregion

    #region Initialization

    public BuildGraphView()
    {
        // 基础交互操作
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());
        this.AddManipulator(new ClickSelector());

        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

        // 网格背景，置于最底层
        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

        graphViewChanged += OnGraphChanged;
    }

    #endregion

    #region Context Menu

    /// <summary>右键菜单：列出所有可选 Task（排除已存在和主干 Task），点击即添加到 SO。</summary>
    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        base.BuildContextualMenu(evt);

        BuildTaskNode taskNode = FindTaskNode(evt.target as VisualElement);
        if (taskNode != null)
        {
            DropdownMenuAction.Status sourceStatus = taskNode.IsValid
                ? DropdownMenuAction.Status.Normal
                : DropdownMenuAction.Status.Disabled;
            evt.menu.AppendAction("Open Source", _ => OpenTaskSource(taskNode.TaskName), sourceStatus);
            evt.menu.AppendSeparator();
        }

        if (_isBuildRunning)
        {
            evt.menu.AppendAction(
                "Create Task / Build is running",
                null,
                DropdownMenuAction.Status.Disabled);
            return;
        }

        if (_config == null)
        {
            evt.menu.AppendAction(
                "Create Task / No BuildPipelineConfig",
                null,
                DropdownMenuAction.Status.Disabled);
            return;
        }

        HashSet<string> existing = _config.Tasks != null
            ? new HashSet<string>(_config.Tasks.Select(e => e.TaskName), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        string[] candidates = BuildTaskResolver.GetTaskNames()
            .Where(name => !BuildPipelineBackbone.IsBackboneTask(name) && !existing.Contains(name))
            .ToArray();

        if (candidates.Length == 0)
        {
            evt.menu.AppendAction(
                "Create Task / No optional task available",
                null,
                DropdownMenuAction.Status.Disabled);
            return;
        }

        foreach (string taskName in candidates)
        {
            evt.menu.AppendAction($"Create Task/{taskName}", _ => AddTaskEntry(taskName));
        }
    }

    /// <summary>
    /// 全量重建 DAG：读取 SO 配置 → 解析 Task 实例 → 计算布局 → 创建节点 → 连线。
    /// Reload 期间临时放行 graphViewChanged，完成后再恢复只读保护。
    /// </summary>
    public void Reload(BuildPipelineConfig config)
    {
        _config = config;
        _isReloading = true;
        DeleteElements(graphElements.ToList());
        _isReloading = false;
        _nodeMap.Clear();

        if (config == null || config.Tasks == null || config.Tasks.Count == 0)
            return;

        // 解析 TaskName → IBuildTask 实例
        // 注意：仅 Enabled 的任务参与调度校验，但 DAG 可视化展示全部 Task
        var instances = new Dictionary<string, IBuildTask>(StringComparer.Ordinal);
        foreach (var entry in config.Tasks)
        {
            if (BuildTaskResolver.Exists(entry.TaskName))
            {
                try
                {
                    instances[entry.TaskName] = BuildTaskResolver.CreateTask(entry.TaskName);
                }
                catch
                {
                    /* 构造失败是已知边界：用户可能删除 Task 类但 SO 仍引用其名称 */
                }
            }
        }

        // 计算合并依赖 → 布局
        var deps = ComputeMergedDeps(config, instances);
        var positions = BuildGraphLayoutEngine.ComputeLayout(config.Tasks, deps);

        // 创建节点
        foreach (var entry in config.Tasks)
        {
            instances.TryGetValue(entry.TaskName, out var task);
            var node = new BuildTaskNode(entry, task);
            _nodeMap[entry.TaskName] = node;
            AddElement(node);

            if (positions.TryGetValue(entry.TaskName, out var pos))
                node.SetPosition(new Rect(pos.x, pos.y, 0, 0));
        }

        // 数据流边先创建并下沉到最底层，执行依赖边后创建在上层
        // 保证主干执行依赖保持最高可读性，数据流作为辅助参考
        CreateDataFlowEdges(instances, deps);
        CreateExecutionEdges(config, deps);
        CreateLegend();
    }

    public void SetBuildRunning(bool running)
    {
        _isBuildRunning = running;
    }

    public void ResetExecutionStatuses()
    {
        foreach (var node in _nodeMap.Values)
            node.ResetExecutionStatus();
    }

    public void SetTaskExecutionStatus(BuildTaskExecutionEvent evt)
    {
        if (string.IsNullOrEmpty(evt.TaskName))
            return;

        if (_nodeMap.TryGetValue(evt.TaskName, out var node))
            node.SetExecutionStatus(evt.Status, evt.Result);
    }

    /// <summary>添加可选 Task 到 SO，触发 Undo 回滚支持和 Reload 刷新</summary>
    private void AddTaskEntry(string taskName)
    {
        if (_config == null || _isBuildRunning)
            return;

        Undo.RecordObject(_config, "Create Build Task Entry");
        _config.Tasks ??= new List<TaskEntry>();
        _config.Tasks.Add(new TaskEntry { TaskName = taskName, Enabled = true });
        EditorUtility.SetDirty(_config);
        Reload(_config);
        OnConfigChanged?.Invoke();
    }

    /// <summary>从右键事件目标向上查找被点击的 Task 节点。</summary>
    private static BuildTaskNode FindTaskNode(VisualElement element)
    {
        while (element != null)
        {
            if (element is BuildTaskNode node)
                return node;
            element = element.parent;
        }

        return null;
    }

    /// <summary>定位并打开 Task 对应的 C# 源码。</summary>
    private static void OpenTaskSource(string taskName)
    {
        if (!BuildTaskResolver.TryGetTaskType(taskName, out Type taskType))
        {
            Debug.LogWarning($"[BuildGraphView] 未找到 Task 源码：TaskName={taskName} 未注册。");
            return;
        }

        string[] guids = AssetDatabase.FindAssets($"{taskType.Name} t:MonoScript");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == taskType)
            {
                AssetDatabase.OpenAsset(script);
                return;
            }
        }

        Debug.LogWarning($"[BuildGraphView] 未找到 Task 源码：TaskName={taskName}, Type={taskType.FullName}。");
    }

    #endregion

    #region Legend

    /// <summary>图例：Code（灰白）= 代码依赖，SO（蓝）= SO 配置依赖，Data（绿）= 数据流</summary>
    private void CreateLegend()
    {
        var legend = new VisualElement();
        legend.style.position = Position.Absolute;
        legend.style.right = 12;
        legend.style.bottom = 12;
        legend.style.flexDirection = FlexDirection.Row;
        legend.style.paddingLeft = 8;
        legend.style.paddingRight = 8;
        legend.style.paddingTop = 4;
        legend.style.paddingBottom = 4;
        legend.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
        legend.pickingMode = PickingMode.Ignore;

        legend.Add(CreateLegendItem("Code", new Color(0.85f, 0.85f, 0.85f)));
        legend.Add(CreateLegendItem("SO", new Color(0.3f, 0.65f, 1f)));
        legend.Add(CreateLegendItem("Data", new Color(0.35f, 0.9f, 0.45f)));
        Add(legend);
    }

    /// <summary>创建图例单项：色条 + 文字标签</summary>
    private static VisualElement CreateLegendItem(string text, Color color)
    {
        var item = new VisualElement();
        item.style.flexDirection = FlexDirection.Row;
        item.style.alignItems = Align.Center;
        item.style.marginRight = 10;

        var line = new VisualElement();
        line.style.width = 20;
        line.style.height = 2;
        line.style.marginRight = 4;
        line.style.backgroundColor = color;
        item.Add(line);

        var label = new Label(text);
        label.style.fontSize = 10;
        label.style.color = new Color(0.78f, 0.78f, 0.78f);
        item.Add(label);

        return item;
    }

    #endregion

    #region Read-Only Guard

    /// <summary>拦截所有增删操作保持只读。Reload 期间 _isReloading=true 时放行。</summary>
    private GraphViewChange OnGraphChanged(GraphViewChange change)
    {
        if (_isReloading) return change;
        // 清空增删列表但保留 move 事件，允许用户拖动节点位置
        if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
            change.edgesToCreate.Clear();
        if (change.elementsToRemove != null && change.elementsToRemove.Count > 0)
            change.elementsToRemove.Clear();
        return change;
    }

    /// <summary>返回空列表禁止用户手动连线——DAG 边由 Reload 自动生成。</summary>
    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        return new List<Port>();
    }

    #endregion

    #region Edge Creation

    /// <summary>创建执行依赖边：代码依赖（灰白）或 SO 配置依赖（蓝）</summary>
    private void CreateExecutionEdges(BuildPipelineConfig config,
        Dictionary<string, string[]> mergedDeps)
    {
        foreach (var kv in mergedDeps)
        {
            string taskName = kv.Key;
            if (!_nodeMap.TryGetValue(taskName, out var node))
                continue;
            foreach (var depName in kv.Value)
            {
                if (!_nodeMap.TryGetValue(depName, out var depNode))
                    continue;

                bool fromSO = IsSODependency(config, depName, taskName);
                var style = fromSO ? EdgeStyles.SODependency : EdgeStyles.CodeDependency;

                var edge = new Edge
                {
                    output = depNode.ExecOutput,
                    input = node.ExecInput,
                };
                ApplyEdgeStyle(edge, style);
                AddElement(edge);
            }
        }
    }

    /// <summary>
    /// 创建数据流边：按拓扑顺序连接 ReadKey 到最近上游 WriteKey。
    /// 边先创建然后 SendToBack() 下沉，避免干扰执行依赖边的可读性。
    /// </summary>
    private void CreateDataFlowEdges(Dictionary<string, IBuildTask> instances, Dictionary<string, string[]> deps)
    {
        if (instances.Count == 0) return;

        List<string> orderedTasks = SortTasksForDataFlow(instances, deps);
        var latestWriters = new Dictionary<string, string>(StringComparer.Ordinal);
        var dataFlowPairs = new HashSet<string>(StringComparer.Ordinal);

        foreach (string taskName in orderedTasks)
        {
            if (!instances.TryGetValue(taskName, out IBuildTask task))
                continue;

            if (task.ReadKeys != null)
            {
                foreach (var key in task.ReadKeys)
                {
                    if (latestWriters.TryGetValue(key, out var producerName)
                        && producerName != taskName
                        && _nodeMap.TryGetValue(producerName, out var prodNode)
                        && _nodeMap.TryGetValue(taskName, out var consNode))
                    {
                        string pairKey = MakePairKey(producerName, taskName);
                        if (!dataFlowPairs.Add(pairKey))
                            continue;

                        var edge = new Edge
                        {
                            output = prodNode.DataOutput,
                            input = consNode.DataInput,
                        };
                        ApplyEdgeStyle(edge, EdgeStyles.DataFlow);
                        AddElement(edge);
                        edge.SendToBack();
                    }
                }
            }

            if (task.WriteKeys == null)
                continue;

            foreach (var key in task.WriteKeys)
                latestWriters[key] = taskName;
        }
    }

    /// <summary>判断依赖是否仅来自 SO TaskEntry.DependsOn（非 IBuildTask.DependsOn 代码依赖）</summary>
    private static bool IsSODependency(BuildPipelineConfig config, string depName, string taskName)
    {
        if (config?.Tasks == null) return false;
        var entry = config.Tasks.Find(e => e.TaskName == taskName);
        return entry?.DependsOn != null && entry.DependsOn.Contains(depName);
    }

    /// <summary>应用边样式：禁用交互 + 设置颜色/透明度</summary>
    private static void ApplyEdgeStyle(Edge edge, EdgeStyle style)
    {
        // 所有边均禁用选中、删除、移动
        edge.capabilities &= ~Capabilities.Selectable;
        edge.capabilities &= ~Capabilities.Deletable;
        edge.capabilities &= ~Capabilities.Movable;
        edge.pickingMode = PickingMode.Ignore;

        switch (style)
        {
            case EdgeStyle.CodeDependency:
                ApplyEdgeColor(edge, new Color(0.85f, 0.85f, 0.85f, 0.95f)); // 灰白
                break;
            case EdgeStyle.SODependency:
                ApplyEdgeColor(edge, new Color(0.3f, 0.65f, 1f, 0.95f)); // 蓝
                break;
            case EdgeStyle.DataFlow:
                edge.style.opacity = 0.35f;
                ApplyEdgeColor(edge, new Color(0.35f, 0.9f, 0.45f, 0.35f)); // 绿半透明
                break;
        }
    }

    /// <summary>设置边两端颜色并触发重绘</summary>
    private static void ApplyEdgeColor(Edge edge, Color color)
    {
        if (edge.edgeControl == null)
            return;

        edge.edgeControl.inputColor = color;
        edge.edgeControl.outputColor = color;
        edge.edgeControl.MarkDirtyRepaint();
    }

    #endregion

    #region Helpers

    /// <summary>生成数据流边的去重键："producer->consumer"</summary>
    private static string MakePairKey(string source, string target)
    {
        return string.Concat(source, "->", target);
    }

    /// <summary>按执行依赖推导数据流展示顺序；循环或缺口时回退到现有 Task 顺序。</summary>
    private static List<string> SortTasksForDataFlow(
        Dictionary<string, IBuildTask> instances,
        Dictionary<string, string[]> deps)
    {
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var successors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string taskName in instances.Keys)
        {
            indegree[taskName] = 0;
            successors[taskName] = new List<string>();
        }

        foreach (var kv in deps)
        {
            if (!instances.ContainsKey(kv.Key) || kv.Value == null)
                continue;

            foreach (string dep in kv.Value)
            {
                if (!instances.ContainsKey(dep))
                    continue;

                successors[dep].Add(kv.Key);
                indegree[kv.Key]++;
            }
        }

        var result = new List<string>();
        var ready = new List<string>();
        foreach (var kv in indegree)
        {
            if (kv.Value == 0)
                ready.Add(kv.Key);
        }
        ready.Sort(StringComparer.Ordinal);

        while (ready.Count > 0)
        {
            string current = ready[0];
            ready.RemoveAt(0);
            result.Add(current);

            foreach (string succ in successors[current])
            {
                indegree[succ]--;
                if (indegree[succ] == 0)
                {
                    ready.Add(succ);
                    ready.Sort(StringComparer.Ordinal);
                }
            }
        }

        return result.Count == instances.Count ? result : instances.Keys.ToList();
    }

    /// <summary>
    /// 合并 IBuildTask.DependsOn（代码定义）与 TaskEntry.DependsOn（SO 面板配置）。
    /// 代码依赖 + SO 配置依赖共同构成最终执行顺序。
    /// </summary>
    private static Dictionary<string, string[]> ComputeMergedDeps(
        BuildPipelineConfig config,
        Dictionary<string, IBuildTask> instances)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var entry in config.Tasks)
        {
            var deps = new List<string>();

            // 代码级依赖（IBuildTask.DependsOn）
            if (instances.TryGetValue(entry.TaskName, out var task) && task.DependsOn != null)
                deps.AddRange(task.DependsOn);

            // SO 面板级依赖（TaskEntry.DependsOn），去重追加
            if (entry.DependsOn != null)
            {
                foreach (var dep in entry.DependsOn)
                    if (!deps.Contains(dep))
                        deps.Add(dep);
            }

            result[entry.TaskName] = deps.ToArray();
        }

        return result;
    }

    #endregion
}
