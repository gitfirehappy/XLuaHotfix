using UnityEngine;

[CreateAssetMenu(fileName = "UIResourceConfig", menuName = "UI/Resource Config")]
public class UIResourceConfigSO : ScriptableObject
{
    [System.Serializable]
    public class UIGroupDefinition
    {
        [Header("组ID")]
        public string groupID;

        [Header("静态UI预制体")]
        public GameObject[] manualUIForms;

        [Header("动态面板模板（实例化）")]
        [FormerlySerializedAs("additionalPreloadForms")]
        public GameObject[] dynamicFormTemplates;
    }

    [System.Serializable]
    public class UIRegistrationGroup
    {
        [Header("父Canvas名称（场景中Canvas物体的名字）")]
        public string parentCanvasName;

        [Header("分组配置")]
        public UIGroupDefinition[] uiGroups;
    }

    [Header("UI注册组配置")]
    public UIRegistrationGroup[] uiRegistrationGroups;
}