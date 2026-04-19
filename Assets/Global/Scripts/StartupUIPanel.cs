using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement; // 用于重试逻辑

/// <summary>
/// 启动界面 UI 控制器
/// 整合了进度显示、状态反馈及自适应隐藏逻辑
/// </summary>
public class StartupUIPanel : MonoBehaviour
{
    [Header("Components")]
    [Tooltip("进度条 Slider")]
    [SerializeField] private Slider progressSlider;
    
    [Tooltip("当前步骤文本 (e.g. 'Downloading Bundles...')")]
    [SerializeField] private TextMeshProUGUI stepLabel;
    
    [Tooltip("百分比文本 (e.g. '85%')")]
    [SerializeField] private TextMeshProUGUI progressLabel;
    
    [Tooltip("错误信息文本")]
    [SerializeField] private TextMeshProUGUI errorLabel;
    
    [Tooltip("重试按钮 (可选，当出错时显示)")]
    [SerializeField] private Button retryButton;

    [Header("Settings")]
    [Tooltip("加载完成后是否自动隐藏")]
    [SerializeField] private bool hideOnLaunch = true;
    
    private void Awake()
    {
        // 初始状态清理
        if (errorLabel != null) errorLabel.gameObject.SetActive(false);
        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(false);
            retryButton.onClick.AddListener(OnRetryClicked);
        }
    }

    private void OnEnable()
    {
        // 1. 订阅事件
        HotfixManager.OnStepChanged += HandleStepChanged;
        HotfixManager.OnProgress += HandleProgress;
        HotfixManager.OnError += HandleError;
        HotfixManager.OnFinished += HandleFinished;

        // 2. 关键点：立即同步当前 HotfixManager 的状态
        // 防止 GameLauncher 运行过快，导致 UI 错过前几个事件
        SyncStateImmediate();
    }

    private void OnDisable()
    {
        HotfixManager.OnStepChanged -= HandleStepChanged;
        HotfixManager.OnProgress -= HandleProgress;
        HotfixManager.OnError -= HandleError;
        HotfixManager.OnFinished -= HandleFinished;
    }

    private async void Start()
    {
        if (!hideOnLaunch) return;
        
        // 等待整个游戏启动信号
        await LaunchSignal.WaitForLaunch();
        
        // 启动完成后隐藏面板
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 主动拉取 Manager 状态进行 UI 刷新
    /// </summary>
    private void SyncStateImmediate()
    {
        // 获取当前步骤名
        string currentStep = HotfixManager.CurrentStepName;
        // 获取当前进度
        float currentProgress = HotfixManager.CurrentProgressValue;
        
        // 如果还没有开始（空字符串），可以显示默认文本
        if (string.IsNullOrEmpty(currentStep) && currentProgress <= 0f)
        {
            UpdateUI(0f, "Initializing...");
        }
        else
        {
            UpdateUI(currentProgress, currentStep);
        }
    }

    // --- Event Handlers ---

    private void HandleStepChanged(string stepName)
    {
        // 步骤变化时，进度条保持当前值，只更新文字
        // 这里的进度值通常由 OnProgress 驱动，这里仅更新文本
        if (stepLabel != null) stepLabel.text = stepName;
    }

    private void HandleProgress(float progress, string stepName)
    {
        UpdateUI(progress, stepName);
    }

    private void HandleError(string message)
    {
        if (errorLabel != null)
        {
            errorLabel.gameObject.SetActive(true);
            errorLabel.text = $"Error: {message}";
        }

        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(true);
        }
        
        // 可以在这里停止进度条动画等
    }

    private void HandleFinished()
    {
        UpdateUI(1f, "Ready");
    }
    
    // --- UI Logic ---

    private void UpdateUI(float progress01, string stepText)
    {
        float clamped = Mathf.Clamp01(progress01);

        if (progressSlider != null)
            progressSlider.value = clamped;

        if (progressLabel != null)
            progressLabel.text = $"{Mathf.RoundToInt(clamped * 100f)}%";

        if (stepLabel != null && !string.IsNullOrEmpty(stepText))
            stepLabel.text = stepText;
    }

    private void OnRetryClicked()
    {
        // 简单的重试逻辑：重载当前场景
        // 实际项目中可能需要更复杂的重连逻辑，比如 HotfixManager.Retry()
        Debug.Log("Retrying hotfix process...");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}