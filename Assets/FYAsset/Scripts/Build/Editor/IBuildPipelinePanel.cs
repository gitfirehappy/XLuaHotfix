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
