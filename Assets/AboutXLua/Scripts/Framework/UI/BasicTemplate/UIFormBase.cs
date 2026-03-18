using UnityEngine;
using System;
using System.Collections.Generic;
using DG.Tweening;

public class UIFormBase : MonoBehaviour, IUIForm
{
    protected UIManager uIManager;

    public FormState CurrentState { get; private set; } = FormState.Closed;
    public bool IsInited = false;

    [SerializeField]
    private UIFormConfigSO _config;
    public int MajorLayerOrder => _config != null ? _config.majorOrder : 0;
    public int MinorLayerOrder => _config != null ? _config.minorOrder : 0;
    public bool Cached => _config != null ? _config.cached : false;
    public FormAnimType AnimType => _config != null ? _config.animType : FormAnimType.None;
    public float FadeInDuration => _config != null && _config.fadeInDuration > 0 ? _config.fadeInDuration : 0.5f;
    public float FadeOutDuration => _config != null && _config.fadeOutDuration > 0 ? _config.fadeOutDuration : 0.5f;
    public float ZoomScale => _config != null && _config.zoomScale > 0 ? _config.zoomScale : 1f;
    public float SlideOffset => _config != null ? _config.slideOffset : 0f;

    public Vector3 originalLocalPos;

    /// <summary>
    /// 动态面板标识（true = 通过 UIManager.CreateDynamicForm 创建）
    /// 使用场景：Buff卡牌、列表/网格UI、Toast提示队列等需要批量生成的同类面板
    /// </summary>
    [Header("动态面板配置")]
    [SerializeField] private bool isDynamicForm = false;

    /// <summary>
    /// 动态面板所属分组ID，用于 UIManager 查找父 Canvas 和批量管理
    /// 同一 groupID 的面板共享同一个 Canvas，由 UIManager 统一管理显隐
    /// </summary>
    [SerializeField] private string dynamicGroupID = "";

    public bool IsDynamicForm => isDynamicForm;
    public string DynamicGroupID => dynamicGroupID;

    private CanvasGroup _canvasGroup;
    public CanvasGroup CanvasGroup
    {
        get
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            return _canvasGroup;
        }
    }

    private void Awake()
    {
        if (!gameObject.scene.IsValid()) return;

        IUIForm ui = this;
        ui.RegisterForm();

        originalLocalPos = transform.localPosition;

        if (gameObject.activeSelf)
        {
            CloseImmediate();
        }

        if (AnimType == FormAnimType.Fade)
        {
            CanvasGroup.alpha = 0f;
        }
    }

    private void OnDestroy()
    {
        IUIForm ui = this;
        ui.UnRegisterForm();
    }

    public void Open(UIManager uIManager)
    {
        if (CurrentState == FormState.Opened || CurrentState == FormState.Opening) return;

        this.uIManager = uIManager;
        if (!IsInited)
        {
            IsInited = true;
            Init();
        }

        CurrentState = FormState.Opening;
        OpenAnim();
    }

    public void Close()
    {
        if (CurrentState == FormState.Closed || CurrentState == FormState.Closing) return;

        CurrentState = FormState.Closing;
        CloseAnim();
    }

    private void CloseImmediate()
    {
        if (AnimType == FormAnimType.Fade)
        {
            CanvasGroup.alpha = 0f;
        }

        gameObject.SetActive(false);
        CurrentState = FormState.Closed;
    }

    protected virtual void Init() { }

    private void OpenAnim()
    {
        CanvasGroup.blocksRaycasts = false;

        Action onOpenComplete = () =>
        {
            CurrentState = FormState.Opened;
            CanvasGroup.blocksRaycasts = true;
        };

        float duration = FadeInDuration;
        float offset = SlideOffset;

        switch (AnimType)
        {
            case FormAnimType.None:
                gameObject.SetActive(true);
                onOpenComplete();
                break;
            case FormAnimType.Fade:
                UIAnimation.FadeIn(this, onOpenComplete, duration);
                break;
            case FormAnimType.Zoom:
                UIAnimation.ZoomIn(this, onOpenComplete, duration, ZoomScale);
                break;
            case FormAnimType.Pop:
                UIAnimation.PopIn(this, onOpenComplete, duration, ZoomScale);
                break;
            case FormAnimType.SlideLeft:
                UIAnimation.SlideIn(this, new Vector3(-(offset > 0 ? offset : Screen.width), 0, 0), onOpenComplete, duration);
                break;
            case FormAnimType.SlideRight:
                UIAnimation.SlideIn(this, new Vector3(offset > 0 ? offset : Screen.width, 0, 0), onOpenComplete, duration);
                break;
            case FormAnimType.SlideUp:
                UIAnimation.SlideIn(this, new Vector3(0, offset > 0 ? offset : Screen.height, 0), onOpenComplete, duration);
                break;
            case FormAnimType.SlideDown:
                UIAnimation.SlideIn(this, new Vector3(0, -(offset > 0 ? offset : Screen.height), 0), onOpenComplete, duration);
                break;
            case FormAnimType.FadeSlide:
                UIAnimation.FadeSlideIn(this, new Vector3(0, -(offset > 0 ? offset : 100f), 0), onOpenComplete, duration);
                break;
        }
    }

    private void CloseAnim()
    {
        CanvasGroup.blocksRaycasts = false;

        Action onCloseComplete = () =>
        {
            CurrentState = FormState.Closed;

            if (!Cached)
            {
                UIManager.Instance.UnRegisterForm(this);
                Destroy(gameObject);
            }
        };

        float duration = FadeOutDuration;
        float offset = SlideOffset;

        switch (AnimType)
        {
            case FormAnimType.None:
                gameObject.SetActive(false);
                onCloseComplete();
                break;
            case FormAnimType.Fade:
                UIAnimation.FadeOut(this, onCloseComplete, duration);
                break;
            case FormAnimType.Zoom:
                UIAnimation.ZoomOut(this, onCloseComplete, duration);
                break;
            case FormAnimType.Pop:
                UIAnimation.PopOut(this, onCloseComplete, duration);
                break;
            case FormAnimType.SlideLeft:
                UIAnimation.SlideOut(this, new Vector3(-(offset > 0 ? offset : Screen.width), 0, 0), onCloseComplete, duration);
                break;
            case FormAnimType.SlideRight:
                UIAnimation.SlideOut(this, new Vector3(offset > 0 ? offset : Screen.width, 0, 0), onCloseComplete, duration);
                break;
            case FormAnimType.SlideUp:
                UIAnimation.SlideOut(this, new Vector3(0, offset > 0 ? offset : Screen.height, 0), onCloseComplete, duration);
                break;
            case FormAnimType.SlideDown:
                UIAnimation.SlideOut(this, new Vector3(0, -(offset > 0 ? offset : Screen.height), 0), onCloseComplete, duration);
                break;
            case FormAnimType.FadeSlide:
                UIAnimation.FadeOut(this, onCloseComplete, duration);
                break;
        }
    }

    public UIFormBase GetUIFormBase() => this;

    #region 动态生成面板扩展

    public void InitializeAsDynamic(string groupID, UIFormConfigSO config = null)
    {
        isDynamicForm = true;
        dynamicGroupID = groupID;
        if (config != null) _config = config;

        if (!IsInited)
        {
            IsInited = true;
            Init();
        }
    }

    protected Canvas FindCanvasForGroup(string groupID)
    {
        Canvas canvas = UIManager.Instance?.GetCanvasGroup(groupID);
        if (canvas == null)
        {
            Debug.LogError($"Canvas for group '{groupID}' not found!");
        }
        return canvas;
    }

    #endregion

}

public enum FormState
{
    Opening,
    Opened,
    Closing,
    Closed
}