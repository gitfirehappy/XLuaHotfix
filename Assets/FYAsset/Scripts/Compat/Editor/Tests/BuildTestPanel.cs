#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

/// <summary>
/// AA/AB Build Pipeline 共享 Test 页：显式 Target 选择 + Full/Hotfix/Chain。
/// </summary>
public sealed class BuildTestPanel : BuildPipelineUIToolkitPanel
{
    private readonly BuildTestBackend _backend;
    private readonly List<Toggle> _targetToggles = new();
    private Label _stageLabel;
    private Label _resultLabel;
    private Button _fullButton;
    private Button _hotfixButton;
    private Button _chainButton;
    private Button _e2eFullButton;
    private Button _e2eHotfixButton;
    private Button _e2eChainButton;
    private Button _e2eStandaloneButton;
    private Button _editorSmokeButton;
    private Button _openLogButton;
    private Button _openResultButton;
    private bool _busy;
    private string _lastRunRoot;

    public BuildTestPanel(BuildTestBackend backend)
    {
        _backend = backend;
    }

    public override string PanelName => "Test";

    protected override void BuildContent(VisualElement root)
    {
        VisualElement card = CreateCenteredPanel(root, 560f);
        card.Add(CreateTitle($"{_backend} Build Test"));
        card.Add(CreateBody(
            "Explicit Targets only. Never infers current HotfixUrl / selected Target. " +
            "Always restores project and Targets."));

        card.Add(CreateTitle("Targets"));
        var targetBox = new VisualElement();
        targetBox.style.marginBottom = 8f;
        RebuildTargetToggles(targetBox);
        card.Add(targetBox);

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.marginBottom = 8f;
        _fullButton = new Button(() => Run(BuildTestMode.Full, e2e: false)) { text = "Build Full" };
        _hotfixButton = new Button(() => Run(BuildTestMode.Hotfix, e2e: false)) { text = "Build Hotfix" };
        _chainButton = new Button(() => Run(BuildTestMode.Chain, e2e: false)) { text = "Build Chain" };
        StyleAction(_fullButton);
        StyleAction(_hotfixButton);
        StyleAction(_chainButton);
        actions.Add(_fullButton);
        actions.Add(_hotfixButton);
        actions.Add(_chainButton);
        card.Add(actions);

        var e2eActions = new VisualElement();
        e2eActions.style.flexDirection = FlexDirection.Row;
        e2eActions.style.marginBottom = 8f;
        _e2eFullButton = new Button(() => Run(BuildTestMode.Full, e2e: true)) { text = "E2E Full" };
        _e2eHotfixButton = new Button(() => Run(BuildTestMode.Hotfix, e2e: true)) { text = "E2E Hotfix" };
        _e2eChainButton = new Button(() => Run(BuildTestMode.Chain, e2e: true)) { text = "E2E Chain" };
        _e2eStandaloneButton = new Button(() => Run(BuildTestMode.Standalone, e2e: true)) { text = "E2E Standalone" };
        StyleAction(_e2eFullButton);
        StyleAction(_e2eHotfixButton);
        StyleAction(_e2eChainButton);
        StyleAction(_e2eStandaloneButton);
        e2eActions.Add(_e2eFullButton);
        e2eActions.Add(_e2eHotfixButton);
        e2eActions.Add(_e2eChainButton);
        e2eActions.Add(_e2eStandaloneButton);
        card.Add(e2eActions);

        if (_backend == BuildTestBackend.AB)
        {
            _editorSmokeButton = new Button(EditorPlayModeSmoke.RunMenu) { text = "Editor PlayMode Smoke" };
            StyleAction(_editorSmokeButton);
            card.Add(_editorSmokeButton);
        }

        _stageLabel = CreateBody("Stage: Idle");
        _resultLabel = CreateBody("Last result: n/a");
        card.Add(_stageLabel);
        card.Add(_resultLabel);

        var openRow = new VisualElement();
        openRow.style.flexDirection = FlexDirection.Row;
        _openLogButton = new Button(OpenRunRoot) { text = "Open Run" };
        _openResultButton = new Button(OpenResult) { text = "Open Result" };
        StyleAction(_openLogButton);
        StyleAction(_openResultButton);
        openRow.Add(_openLogButton);
        openRow.Add(_openResultButton);
        card.Add(openRow);

        RefreshEnabled();
    }

    private void RebuildTargetToggles(VisualElement box)
    {
        _targetToggles.Clear();
        box.Clear();
        FYAssetSettings settings = FYAssetSettings.Instance;
        if (settings.PushTargets == null || settings.PushTargets.Count == 0)
        {
            box.Add(CreateBody("No PushTargets configured."));
            return;
        }

        for (int i = 0; i < settings.PushTargets.Count; i++)
        {
            PushTargetConfig config = settings.PushTargets[i];
            if (config == null || string.IsNullOrEmpty(config.Id))
                continue;
            var toggle = new Toggle($"{config.Id} ({config.Type})") { value = false };
            toggle.userData = config.Id;
            toggle.RegisterValueChangedCallback(_ => RefreshEnabled());
            _targetToggles.Add(toggle);
            box.Add(toggle);
        }
    }

    private void Run(BuildTestMode mode, bool e2e)
    {
        if (_busy)
            return;
        if (EditorUtility.IsDirty(0) || IsAnyDirtyAsset())
        {
            EditorUtility.DisplayDialog("Build Test", "Unsaved Scene/Asset changes detected. Save first.", "OK");
            return;
        }

        List<string> targets = CollectSelected(_targetToggles);
        // Standalone E2E 不需要 Target，其余模式必须显式选择
        if (targets.Count == 0 && !(e2e && mode == BuildTestMode.Standalone))
            return;

        List<string> externalTargets = CollectExternalTargets(targets);
        if (externalTargets.Count > 0)
        {
            if (!EditorUtility.DisplayDialog(
                    "External Target",
                    $"This test will temporarily publish to: {string.Join(", ", externalTargets)}. Continue?",
                    "Continue",
                    "Cancel"))
                return;
        }

        _busy = true;
        RefreshEnabled();
        _stageLabel.text = "Stage: Running...";
        _resultLabel.text = "Last result: busy";

        try
        {
            var request = new BuildTestRequest
            {
                Backend = _backend,
                Mode = mode,
                TargetIds = targets,
                ExternalConfirmIds = externalTargets,
                Progress = (stage, msg) =>
                {
                    _stageLabel.text = $"Stage: {stage} - {msg}";
                }
            };
            BuildTestResult result = e2e ? E2ETestEngine.Run(request) : BuildTestEngine.Run(request);
            _lastRunRoot = result.RunRoot;
            _stageLabel.text = $"Stage: Done ({result.FailedStage ?? "complete"})";
            _resultLabel.text =
                $"Last result: {(result.Passed ? "PASS" : "FAIL")} exit={result.ExitCode}\n" +
                $"{result.FirstFailure}\n{result.RunRoot}";
        }
        catch (Exception ex)
        {
            _resultLabel.text = "Last result: exception\n" + ex.Message;
            Debug.LogError(ex);
        }
        finally
        {
            _busy = false;
            RefreshEnabled();
        }
    }

    private static List<string> CollectSelected(List<Toggle> toggles)
    {
        var list = new List<string>();
        for (int i = 0; i < toggles.Count; i++)
        {
            if (toggles[i].value && toggles[i].userData is string id)
                list.Add(id);
        }
        return list;
    }

    private static List<string> CollectExternalTargets(List<string> targetIds)
    {
        var selected = new HashSet<string>(targetIds, StringComparer.OrdinalIgnoreCase);
        var external = new List<string>();
        List<PushTargetConfig> configs = FYAssetSettings.Instance.PushTargets;
        for (int i = 0; configs != null && i < configs.Count; i++)
        {
            PushTargetConfig config = configs[i];
            if (config != null
                && config.Type != PushTargetType.LocalDirectory
                && selected.Contains(config.Id))
            {
                external.Add(config.Id);
            }
        }
        return external;
    }

    private void RefreshEnabled()
    {
        bool hasTarget = false;
        for (int i = 0; i < _targetToggles.Count; i++)
        {
            if (_targetToggles[i].value)
            {
                hasTarget = true;
                break;
            }
        }

        bool enable = !_busy && hasTarget;
        if (_fullButton != null) _fullButton.SetEnabled(enable);
        if (_hotfixButton != null) _hotfixButton.SetEnabled(enable);
        if (_chainButton != null) _chainButton.SetEnabled(enable);
        if (_e2eFullButton != null) _e2eFullButton.SetEnabled(enable);
        if (_e2eHotfixButton != null) _e2eHotfixButton.SetEnabled(enable);
        if (_e2eChainButton != null) _e2eChainButton.SetEnabled(enable);
        // Standalone E2E 仅 AB，且不依赖 Target
        if (_e2eStandaloneButton != null)
            _e2eStandaloneButton.SetEnabled(!_busy && _backend == BuildTestBackend.AB);
        if (_editorSmokeButton != null) _editorSmokeButton.SetEnabled(!_busy);
        if (_openLogButton != null) _openLogButton.SetEnabled(!string.IsNullOrEmpty(_lastRunRoot));
        if (_openResultButton != null) _openResultButton.SetEnabled(!string.IsNullOrEmpty(_lastRunRoot));
    }

    private void OpenRunRoot()
    {
        if (string.IsNullOrEmpty(_lastRunRoot) || !Directory.Exists(_lastRunRoot))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = _lastRunRoot,
            UseShellExecute = true
        });
    }

    private void OpenResult()
    {
        if (string.IsNullOrEmpty(_lastRunRoot))
            return;
        string path = BuildTestPaths.ResultJson(_lastRunRoot);
        if (!File.Exists(path))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static void StyleAction(Button button)
    {
        button.style.flexGrow = 1f;
        button.style.marginRight = 4f;
        button.style.whiteSpace = WhiteSpace.Normal;
    }

    private static bool IsAnyDirtyAsset()
    {
        // EditorUtility.IsDirty(0) covers scene; also block if compiling.
        return EditorApplication.isCompiling || EditorApplication.isUpdating;
    }
}
#endif
