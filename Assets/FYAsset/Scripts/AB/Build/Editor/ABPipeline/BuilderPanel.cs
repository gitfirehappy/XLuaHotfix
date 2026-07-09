using UnityEngine.UIElements;

/// <summary>
/// 构建执行面板预留页；当前构建入口由 AA/AB PipelinePanel 承担。
/// </summary>
public class BuilderPanel : BuildPipelineUIToolkitPanel
{
    public override string PanelName => "Build";

    protected override void BuildContent(VisualElement root)
    {
        VisualElement panel = CreateCenteredPanel(root);
        panel.Add(CreateBody("构建入口预留。"));
    }
}
