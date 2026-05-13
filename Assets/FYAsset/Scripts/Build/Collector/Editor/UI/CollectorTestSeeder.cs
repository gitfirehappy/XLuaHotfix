using UnityEditor;
using UnityEngine;

/// <summary>
/// 测试数据填充工具 —— 一键生成示例 Package/Group/Collector，用于开发期 UI 验证。
/// </summary>
public static class CollectorTestSeeder
{
    /// <summary>通过菜单项向当前 CollectorSetting SO 写入一组示例数据</summary>
    [MenuItem("XLua/Debug/Seed CollectorSetting Test Data")]
    private static void Seed()
    {
        var setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(
            FYAssetSettings.Instance.CollectorSettingPath);

        if (setting == null)
        {
            Debug.LogError("CollectorSetting not found at " + FYAssetSettings.Instance.CollectorSettingPath);
            return;
        }

        Undo.RecordObject(setting, "Seed CollectorSetting Test Data");

        setting.Packages.Clear();

        var pkg = new CollectorPackage
        {
            PackageName = "DefaultPackage",
            SharePolicy = new SharePolicyConfig { MinReferenceCount = 2 }
        };

        var artGroup = new CollectorGroup { GroupName = "Art", Enabled = true };
        artGroup.Collectors.Add(new Collector
        {
            CollectPath = "Assets/Art",
            CollectorType = ECollectorType.Main,
            ForcePayloadKind = EForcePayloadKind.Auto,
            AddressRuleName = "AddressByFileName",
            PackRuleName = "PackByDirectory",
            FilterRuleName = "CollectAll",
            GroupRuleName = "GroupAll"
        });

        var prefabGroup = new CollectorGroup { GroupName = "Prefab", Enabled = true };
        prefabGroup.Collectors.Add(new Collector
        {
            CollectPath = "Assets/Prefab",
            CollectorType = ECollectorType.Main,
            ForcePayloadKind = EForcePayloadKind.Auto,
            AddressRuleName = "AddressByFileName",
            PackRuleName = "PackSeparately",
            FilterRuleName = "CollectAll",
            GroupRuleName = "GroupAll"
        });

        pkg.Groups.Add(artGroup);
        pkg.Groups.Add(prefabGroup);
        setting.Packages.Add(pkg);

        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();
        CollectorReverseIndex.Instance.MarkDirty();
        Debug.Log("[CollectorTestSeeder] Seeded 1 package, 2 groups, 2 collectors.");
    }
}
