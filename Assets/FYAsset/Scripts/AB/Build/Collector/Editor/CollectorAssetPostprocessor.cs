using UnityEditor;

/// <summary>
/// 监听资产导入/删除/移动，标记 Collector 反向索引失效。
/// </summary>
public class CollectorAssetPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if ((importedAssets != null && importedAssets.Length > 0) ||
            (deletedAssets != null && deletedAssets.Length > 0) ||
            (movedAssets != null && movedAssets.Length > 0) ||
            (movedFromAssetPaths != null && movedFromAssetPaths.Length > 0))
        {
            CollectorReverseIndex.Instance.MarkDirty();
        }
    }
}
