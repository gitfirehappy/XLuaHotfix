using UnityEngine;

[CreateAssetMenu(fileName = "UIFormConfig", menuName = "UI/UI Form Config", order = 1)]
public class UIFormConfigSO : ScriptableObject
{
    [Header("显示名称")]
    public string displayName = "Default";

    [Header("层级配置")]
    public int majorOrder = 0;
    public int minorOrder = 0;

    [Header("行为配置")]
    public bool cached = false;
    public FormAnimType animType = FormAnimType.None;

    [Header("动画参数（0 = 使用内置默认值）")]
    [Tooltip("淡入时长（秒）")]
    public float fadeInDuration = 0f;
    [Tooltip("淡出时长（秒）")]
    public float fadeOutDuration = 0f;
    [Tooltip("缩放/弹入目标倍率（Pop/Zoom 动画）")]
    public float zoomScale = 0f;
    [Tooltip("Slide/FadeSlide 的初始偏移量（像素，0 = 使用屏幕宽/高）")]
    public float slideOffset = 0f;
}