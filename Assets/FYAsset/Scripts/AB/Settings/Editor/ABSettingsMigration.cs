using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一次性迁移：将旧版 FYAssetSettings 资产中的 PlayMode 序列化值写入 FYAssetABSettings。
/// 迁移标记是旧资产 YAML 中不再存在 PlayMode 行，因此已迁移的项目不会重复执行。
/// </summary>
[InitializeOnLoad]
internal static class ABSettingsMigration
{
    private static readonly Regex PlayModeLineRegex =
        new Regex(@"^[ \t]*PlayMode:[ \t]*(\d+)[ \t]*\r?$", RegexOptions.Multiline);

    static ABSettingsMigration()
    {
        EditorApplication.delayCall += MigratePlayMode;
    }

    internal static void MigratePlayMode()
    {
        string oldPath = FYAssetSettings.DEFAULT_ASSET_PATH;
        if (!File.Exists(oldPath))
            return;

        string yaml = File.ReadAllText(oldPath);
        Match match = PlayModeLineRegex.Match(yaml);
        if (!match.Success)
            return;

        var abSettings = FYAssetABSettings.Instance;
        int value = int.Parse(match.Groups[1].Value);
        abSettings.PlayMode = System.Enum.IsDefined(typeof(EPlayMode), value)
            ? (EPlayMode)value
            : EPlayMode.Runtime;

        EditorUtility.SetDirty(abSettings);

        string stripped = PlayModeLineRegex.Replace(yaml, string.Empty);
        if (stripped != yaml)
        {
            // 旧资产与代码同步修订，避免每次启动被再次导出序列化行。
            File.WriteAllText(oldPath, stripped);
            AssetDatabase.ImportAsset(oldPath, ImportAssetOptions.ForceSynchronousImport);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[ABSettingsMigration] PlayMode 已从 FYAssetSettings 迁移到 FYAssetABSettings（值={abSettings.PlayMode}）。");
    }
}
