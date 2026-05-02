using UnityEditor;
using UnityEngine;

public static class CollectorTestSeeder
{
    [MenuItem("XLua/Debug/Seed CollectorSetting Test Data")]
    private static void Seed()
    {
        var setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(
            FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);

        if (setting == null)
        {
            Debug.LogError("CollectorSetting not found at " + FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
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
        Debug.Log("[CollectorTestSeeder] Seeded 1 package, 2 groups, 2 collectors.");
    }
}
