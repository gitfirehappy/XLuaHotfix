using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

/// <summary>
/// CollectorSetting 的自定义 Inspector。
/// 在默认字段之外提供 BuildPipelineWindow 快捷入口。
/// </summary>
[CustomEditor(typeof(CollectorSetting))]
public class CollectorSettingInspector : Editor
{
    /// <summary>
    /// 使用 UI Toolkit 生成 Inspector，并保留原始 Serialized 字段折叠区。
    /// </summary>
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.style.paddingTop = 10f;

        var openButton = new Button(() => EditorApplication.ExecuteMenuItem(FYAssetSettings.BUILD_PIPELINE_WINDOW_MENU_PATH))
        {
            text = "Open Build Pipeline Window"
        };
        openButton.style.height = 40f;
        openButton.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
        root.Add(openButton);

        var foldout = new Foldout
        {
            text = "Show Raw Serialized Fields",
            value = false
        };
        InspectorElement.FillDefaultInspector(foldout, serializedObject, this);
        root.Add(foldout);
        root.TrackSerializedObjectValue(serializedObject, _ => CollectorReverseIndex.Instance.MarkDirty());
        return root;
    }
}
