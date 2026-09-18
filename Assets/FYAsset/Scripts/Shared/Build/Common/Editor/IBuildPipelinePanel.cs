using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// Build Pipeline window 子面板协议。
/// 面板统一通过 UI Toolkit 渲染，并由 backend-specific window 承载。
/// </summary>
public interface IBuildPipelinePanel
{
    string PanelName { get; }
    void OnEnable(EditorWindow window);
    VisualElement CreateContent();
    void OnDisable();
}

/// <summary>
/// 可选可见性回调。
/// 供需要感知显示/隐藏生命周期的面板实现。
/// </summary>
public interface IBuildPipelinePanelVisibility
{
    void SetVisible(bool visible);
}
