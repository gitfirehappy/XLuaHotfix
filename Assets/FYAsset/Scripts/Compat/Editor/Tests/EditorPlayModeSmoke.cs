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
        bool oldUseAb = FYAssetSettings.Instance.UseABBackend;
        EPlayMode oldMode = FYAssetSettings.Instance.PlayMode;
        try
        {
            FYAssetSettings.Instance.UseABBackend = true;
            FYAssetSettings.Instance.PlayMode = EPlayMode.Editor;
            EditorUtility.SetDirty(FYAssetSettings.Instance);
            AssetDatabase.SaveAssets();

            // 新 Instance 状态：ABPackageManager 是单例且已初始化会短路，
            // 冒烟前用反射清 _isInitialized 不划算；依赖首次初始化路径。
            // 若已初始化过，提示重启 Domain / 重进 Play。
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
            FYAssetSettings.Instance.UseABBackend = oldUseAb;
            FYAssetSettings.Instance.PlayMode = oldMode;
            EditorUtility.SetDirty(FYAssetSettings.Instance);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
