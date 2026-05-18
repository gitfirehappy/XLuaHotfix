using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// BuildGraph 中一个 Task 的可视节点。
/// 展示 TaskName、Enabled 状态、ReadKeys/WriteKeys，并提供执行端口和数据端口。
/// 端口仅用于 DAG 可视化显示，不支持用户手动连线。
/// </summary>
public class BuildTaskNode : Node
{
    #region Ports

    /// <summary>执行依赖输入端口（上游 Task -> 本 Task）</summary>
    public Port ExecInput { get; private set; }
    /// <summary>执行依赖输出端口（本 Task -> 下游 Task）</summary>
    public Port ExecOutput { get; private set; }
    /// <summary>数据依赖输入端口（上游写入 -> 本 Task 读取）</summary>
    public Port DataInput { get; private set; }
    /// <summary>数据依赖输出端口（本 Task 写入 -> 下游读取）</summary>
    public Port DataOutput { get; private set; }

    #endregion

    #region Properties

    public string TaskName { get; }
    /// <summary>Task 是否已解析到有效 IBuildTask 实现</summary>
    public bool IsValid { get; }

    #endregion

    #region Colors

    private static readonly Color DisabledColor = new(0.35f, 0.35f, 0.35f);
    private static readonly Color InvalidColor = new(0.55f, 0.2f, 0.2f);
    private static readonly Color ExecColor = new(0.85f, 0.85f, 0.85f);
    private static readonly Color DataColor = new(0.35f, 0.9f, 0.45f);
    private static readonly Color PendingColor = new(0.2f, 0.2f, 0.2f);
    private static readonly Color RunningColor = new(0.95f, 0.65f, 0.2f);
    private static readonly Color SuccessColor = new(0.25f, 0.55f, 0.28f);
    private static readonly Color FailedColor = new(0.65f, 0.18f, 0.18f);
    private static readonly Color SkippedColor = new(0.32f, 0.32f, 0.32f);

    #endregion

    #region State

    private readonly Label _statusLabel;
    private readonly bool _isEnabled;

    #endregion

    #region Construction

    /// <summary>
    /// 构造 Node 并组装 UI 结构：标题、端口（Exec In/Out、Data In/Out）、
    /// 信息面板（Disabled 提示 / 解析失败 / ReadKeys / WriteKeys / SO Deps）、视觉状态。
    /// 端口统一设为只读（pickingMode=Ignore），由 BuildGraphView.GetCompatiblePorts 控制连线匹配。
    /// </summary>
    public BuildTaskNode(TaskEntry entry, IBuildTask task)
    {
        TaskName = entry.TaskName;
        IsValid = task != null;
        _isEnabled = entry.Enabled;

        // ── 标题 ──
        title = entry.TaskName;
        if (!entry.Enabled)
            title += " [DISABLED]";
        if (!IsValid)
            title += " [UNRESOLVED]";

        // ── 端口 ──
        ExecInput = Port.Create<Edge>(Orientation.Horizontal, Direction.Input,
            Port.Capacity.Multi, typeof(object));
        ExecInput.portName = "Exec In";
        ConfigureReadOnlyPort(ExecInput, ExecColor);
        inputContainer.Add(ExecInput);

        DataInput = Port.Create<Edge>(Orientation.Horizontal, Direction.Input,
            Port.Capacity.Multi, typeof(object));
        DataInput.portName = "Data In";
        ConfigureReadOnlyPort(DataInput, DataColor);
        inputContainer.Add(DataInput);

        ExecOutput = Port.Create<Edge>(Orientation.Horizontal, Direction.Output,
            Port.Capacity.Multi, typeof(object));
        ExecOutput.portName = "Exec Out";
        ConfigureReadOnlyPort(ExecOutput, ExecColor);
        outputContainer.Add(ExecOutput);

        DataOutput = Port.Create<Edge>(Orientation.Horizontal, Direction.Output,
            Port.Capacity.Multi, typeof(object));
        DataOutput.portName = "Data Out";
        ConfigureReadOnlyPort(DataOutput, DataColor);
        outputContainer.Add(DataOutput);

        // ── 主体内容 ──
        var infoContainer = new VisualElement();
        infoContainer.style.paddingTop = 4;
        infoContainer.style.paddingBottom = 4;
        infoContainer.style.paddingLeft = 8;
        infoContainer.style.paddingRight = 8;

        if (!entry.Enabled)
        {
            var disabledLabel = new Label("DISABLED");
            disabledLabel.style.color = DisabledColor;
            disabledLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            infoContainer.Add(disabledLabel);
        }

        if (!IsValid)
        {
            var invalidLabel = new Label("No IBuildTask implementation found");
            invalidLabel.style.color = InvalidColor;
            invalidLabel.style.fontSize = 10;
            invalidLabel.style.whiteSpace = WhiteSpace.Normal;
            infoContainer.Add(invalidLabel);
        }
        else
        {
            _statusLabel = new Label("Status: Idle");
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new Color(0.65f, 0.65f, 0.65f);
            infoContainer.Add(_statusLabel);

            if (task.ReadKeys != null && task.ReadKeys.Length > 0)
            {
                var readLabel = new Label("Read: " + string.Join(", ", task.ReadKeys));
                readLabel.style.fontSize = 10;
                readLabel.style.color = new Color(0.6f, 0.8f, 0.6f);
                readLabel.style.whiteSpace = WhiteSpace.Normal;
                infoContainer.Add(readLabel);
            }

            if (task.WriteKeys != null && task.WriteKeys.Length > 0)
            {
                var writeLabel = new Label("Write: " + string.Join(", ", task.WriteKeys));
                writeLabel.style.fontSize = 10;
                writeLabel.style.color = new Color(0.8f, 0.7f, 0.4f);
                writeLabel.style.whiteSpace = WhiteSpace.Normal;
                infoContainer.Add(writeLabel);
            }
        }

        // SO 级 DependsOn
        if (entry.DependsOn != null && entry.DependsOn.Count > 0)
        {
            var soDepLabel = new Label("SO Deps: " + string.Join(", ", entry.DependsOn));
            soDepLabel.style.fontSize = 9;
            soDepLabel.style.color = new Color(0.5f, 0.6f, 0.8f);
            soDepLabel.style.whiteSpace = WhiteSpace.Normal;
            infoContainer.Add(soDepLabel);
        }

        mainContainer.Add(infoContainer);

        // ── 视觉状态 ──
        if (!IsValid)
        {
            titleContainer.style.backgroundColor = InvalidColor;
            titleContainer.style.color = Color.white;
        }
        else if (!entry.Enabled)
        {
            titleContainer.style.backgroundColor = DisabledColor;
            titleContainer.style.color = new Color(0.7f, 0.7f, 0.7f);
        }
        else
        {
            titleContainer.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        }

        // 只读模式：所有端口连接性由 BuildGraphView.GetCompatiblePorts 控制，用户无法拖线
    }

    #endregion

    #region Execution Status

    /// <summary>
    /// 设置节点执行状态颜色。通过 titleContainer 背景色区分：
    /// Pending（深灰）、Running（橙黄）、Success（绿）、Failed（红）、Skipped（灰）。
    /// StatusLabel 显示状态名 + ErrorCode（如有）。
    /// </summary>
    public void SetExecutionStatus(BuildTaskExecutionStatus status, BuildTaskResult result = null)
    {
        if (!_isEnabled || !IsValid)
            return;

        if (_statusLabel != null)
        {
            _statusLabel.text = result != null && !result.Success && !string.IsNullOrEmpty(result.ErrorCode)
                ? $"Status: {status} ({result.ErrorCode})"
                : $"Status: {status}";
        }

        switch (status)
        {
            case BuildTaskExecutionStatus.Pending:
                titleContainer.style.backgroundColor = PendingColor;
                break;
            case BuildTaskExecutionStatus.Running:
                titleContainer.style.backgroundColor = RunningColor;
                break;
            case BuildTaskExecutionStatus.Success:
                titleContainer.style.backgroundColor = SuccessColor;
                break;
            case BuildTaskExecutionStatus.Failed:
                titleContainer.style.backgroundColor = FailedColor;
                break;
            case BuildTaskExecutionStatus.Skipped:
                titleContainer.style.backgroundColor = SkippedColor;
                break;
        }
        MarkDirtyRepaint();
    }

    /// <summary>
    /// 重置节点为 Idle 初始状态（深灰背景，无状态文字）。
    /// </summary>
    public void ResetExecutionStatus()
    {
        if (!_isEnabled || !IsValid)
            return;

        if (_statusLabel != null)
            _statusLabel.text = "Status: Idle";
        titleContainer.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        MarkDirtyRepaint();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 配置端口为只读：设置颜色 + 禁用 pickingMode / Selectable / Deletable / Movable。
    /// </summary>
    private static void ConfigureReadOnlyPort(Port port, Color color)
    {
        port.portColor = color;
        port.pickingMode = PickingMode.Ignore;
        port.capabilities &= ~Capabilities.Selectable;
        port.capabilities &= ~Capabilities.Deletable;
        port.capabilities &= ~Capabilities.Movable;
    }

    #endregion
}
