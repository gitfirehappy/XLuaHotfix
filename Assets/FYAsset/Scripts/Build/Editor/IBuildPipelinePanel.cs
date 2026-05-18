using UnityEditor;

/// <summary>
/// BuildPipelineWindow 子面板统一接口。
/// 每个功能区域（Collector / Pipeline / Builder / Inspector / Settings）分别实现。
/// </summary>
public interface IBuildPipelinePanel
{
    string PanelName { get; }
    void OnEnable(EditorWindow window);
    void OnGUI(UnityEngine.Rect rect);
    void OnDisable();
}

/// <summary>
/// 可见性回调接口。用于 IMGUI 面板切换时显式控制 UI Toolkit 覆盖层，
/// 避免面板自行通过 EditorApplication.update 猜测显示状态。
/// </summary>
public interface IBuildPipelinePanelVisibility
{
    void SetVisible(bool visible);
}
