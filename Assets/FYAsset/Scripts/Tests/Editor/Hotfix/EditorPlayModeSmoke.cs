#if UNITY_EDITOR
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor PlayMode 最小冒烟：构建虚拟索引并加载一个夹具 address。
/// </summary>
public static class EditorPlayModeSmoke
{
    public static async void RunMenu()
    {
        bool ok = await RunAsync();
        EditorUtility.DisplayDialog(
            "Editor PlayMode Smoke",
            ok ? "PASS" : "FAIL — 见 Console",
            "OK");
    }

    public static async Task<bool> RunAsync()
    {
        BackendMode oldBackend = BuildTestState.GetBackendSettings().Backend;
        EPlayMode oldMode = FYAssetABSettings.Instance.PlayMode;
        try
        {
            BuildTestState.GetBackendSettings().Backend = BackendMode.ABManifest;
            FYAssetABSettings.Instance.PlayMode = EPlayMode.Editor;
            EditorUtility.SetDirty(FYAssetABSettings.Instance);
            EditorUtility.SetDirty(BuildTestState.GetBackendSettings());
            AssetDatabase.SaveAssets();

            // ABPackageManager 单例已初始化时 InitializePackageAsync 会短路，
            // 本冒烟依赖首次初始化路径；已初始化过需重启 Domain / 重进 Play。
            bool inited = await ABPackageManager.Instance.InitializePackageAsync();
            if (!inited)
            {
                Debug.LogError("[EditorPlayModeSmoke] InitializePackageAsync 失败");
                return false;
            }

            var (asset, err) = ABPackageManager.Instance
                .LoadAssetSync<TextAsset>(BuildTestConstants.AddressSync);
            if (err != null && err.Severity == RuntimeSeverity.Error)
            {
                Debug.LogError("[EditorPlayModeSmoke] Load 失败: " + err);
                return false;
            }
            if (asset == null)
            {
                Debug.LogError("[EditorPlayModeSmoke] asset 为 null");
                return false;
            }

            Debug.Log($"[EditorPlayModeSmoke] PASS text={asset.text}");
            return true;
        }
        finally
        {
            BuildTestState.GetBackendSettings().Backend = oldBackend;
            FYAssetABSettings.Instance.PlayMode = oldMode;
            EditorUtility.SetDirty(FYAssetABSettings.Instance);
            EditorUtility.SetDirty(BuildTestState.GetBackendSettings());
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
