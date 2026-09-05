using System;
using System.Collections.Generic;
using System.IO;

internal static class LabelParityRetirementTests
{
    // RawFile 直发是 AB 独占的 E2E 夹具（FYAssetPipelineRaw.fyraw 刻意无 importer），
    // AA（Addressables）无等价物，不参与业务标签对等断言。
    private static readonly HashSet<string> AbE2EFixtureAddresses = new(StringComparer.Ordinal)
    {
        "FYAssetPipelineRaw",
    };

    private static readonly string[] RemovedToolFiles =
    {
        "Assets/XLuaFramework/Scripts/Editor/LuaAddressableTagger.cs",
        "Assets/FYAsset/Scripts/Shared/Helpers/Editor/SOAddressableTagger.cs",
        "Assets/FYAsset/Scripts/Shared/Helpers/ScriptObjectContainer.cs",
        "Assets/FYAsset/Scripts/Shared/Helpers/ScriptObjectDataBase.cs"
    };

    private static readonly string[] RetainedTypeConfigFiles =
    {
        "Assets/XLuaFramework/Scripts/LabelBatchManagement/TypeMemberListSO.cs",
        "Assets/SO/LabelConfig/Hotfix.asset",
        "Assets/SO/LabelConfig/LuaCallCSharp.asset",
        "Assets/SO/LabelConfig/CSharpCallLua.asset"
    };

    public static void Run()
    {
        VerifyAAAndABExplicitLabelQueriesMatch();
        VerifyLegacyToolingIsRetired();
        VerifyTypeConfigurationIsPreservedButNotPackaged();
        VerifyLuaBusinessModelHasNoAAOwnership();
    }

    private static void VerifyAAAndABExplicitLabelQueriesMatch()
    {
        Dictionary<string, HashSet<string>> aa = ParseAddressableLabelQueries();
        Dictionary<string, HashSet<string>> ab = ParseABLabelQueries();

        RepoAssert.False(aa.ContainsKey("XLuaConfigs"), "obsolete XLuaConfigs package label must be removed from AA");
        RepoAssert.False(aa.ContainsKey("TypeMemberListSO"), "TypeMemberListSO sources must not remain AA runtime entries");
        RepoAssert.False(ab.ContainsKey("XLuaConfigs"), "obsolete XLuaConfigs package label must not be added to AB");
        RepoAssert.False(ab.ContainsKey("TypeMemberListSO"), "TypeMemberListSO sources must not remain AB runtime entries");
        RepoAssert.SetEqual(aa, ab, "AA and AB explicit business label queries must match");

        RepoAssert.True(aa.TryGetValue("Framework", out HashSet<string> framework) && framework.Contains("StateMachine"),
            "StateMachine must preserve the verified AA Framework label");
        RepoAssert.False(aa.TryGetValue("LuaScriptContainer", out HashSet<string> luaContainers) &&
                         luaContainers.Contains("EventCentre"),
            "orphan EventCentre tool metadata must not create a new AA publication");
    }

    private static void VerifyLegacyToolingIsRetired()
    {
        for (int i = 0; i < RemovedToolFiles.Length; i++)
            RepoAssert.False(RepoSource.Exists(RemovedToolFiles[i]), $"legacy tool must be deleted: {RemovedToolFiles[i]}");

        RepoAssert.False(RepoSource.DirectoryExists("Assets/SO/SOContainer"),
            "tool-only ScriptObjectContainer assets must be deleted");
        RepoAssert.False(RepoSource.Exists("Assets/AddressableAssetsData/AssetGroups/XLuaLabelConfig.asset"),
            "obsolete AA XLuaLabelConfig group must be deleted");
    }

    private static void VerifyTypeConfigurationIsPreservedButNotPackaged()
    {
        for (int i = 0; i < RetainedTypeConfigFiles.Length; i++)
            RepoAssert.True(RepoSource.Exists(RetainedTypeConfigFiles[i]),
                $"real TypeMemberListSO source configuration must remain: {RetainedTypeConfigFiles[i]}");

        string collector = RepoSource.Read("Assets/FYAsset/CollectorData/CollectorSetting.asset");
        RepoAssert.Contains(collector, "- Assets/SO/LabelConfig/**",
            "AB collector must explicitly keep TypeMemberListSO sources out of runtime packages");
        RepoAssert.NotContains(collector, "AssetGUID: 9270edc67bc19564ea89f136cf683452",
            "Hotfix TypeMemberListSO source entry must be removed from AB metadata");
        RepoAssert.NotContains(collector, "AssetGUID: 276eef6c8e9f88545951af44f2803712",
            "LuaCallCSharp TypeMemberListSO source entry must be removed from AB metadata");
        RepoAssert.NotContains(collector, "AssetGUID: a8688d7b8585b454cb58678508d8a564",
            "CSharpCallLua TypeMemberListSO source entry must be removed from AB metadata");
    }

    private static void VerifyLuaBusinessModelHasNoAAOwnership()
    {
        string container = RepoSource.Read("Assets/XLuaFramework/Scripts/LuaScriptContainer.cs");
        string database = RepoSource.Read("Assets/XLuaFramework/Scripts/LuaDataBase.cs");
        string settings = RepoSource.Read("Assets/FYAsset/Scripts/Shared/Settings/FYAssetSettings.cs");

        RepoAssert.NotContains(container, "groupName", "LuaScriptContainer must not own an AA group name");
        RepoAssert.NotContains(container, "addressableLabels", "LuaScriptContainer must not own AA labels");
        RepoAssert.NotContains(container, "ApplyAddressableLabels", "LuaScriptContainer must not write Addressables");
        RepoAssert.NotContains(container, "UnityEditor.AddressableAssets", "Lua business model must not import AA editor APIs");
        RepoAssert.NotContains(database, ".groupName", "LuaDataBase must use package-neutral container identity");
        RepoAssert.NotContains(settings, "DEFAULT_XLUA_TYPE_CONFIG_LOAD_LABEL",
            "dead runtime XLua label constant must be removed");
    }

    private static Dictionary<string, HashSet<string>> ParseAddressableLabelQueries()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string path in RepoSource.EnumerateFiles("Assets/AddressableAssetsData/AssetGroups", "*.asset"))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}Schemas{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            string[] lines = File.ReadAllLines(path);
            string address = null;
            bool inEntry = false;
            bool inLabels = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("  - m_GUID: ", StringComparison.Ordinal))
                {
                    address = null;
                    inEntry = true;
                    inLabels = false;
                    continue;
                }

                if (!inEntry) continue;
                if (line.StartsWith("    m_Address: ", StringComparison.Ordinal))
                {
                    address = line.Substring("    m_Address: ".Length);
                    continue;
                }

                if (line == "    m_SerializedLabels:")
                {
                    inLabels = true;
                    continue;
                }

                if (line.StartsWith("    FlaggedDuringContentUpdateRestriction:", StringComparison.Ordinal))
                {
                    inEntry = false;
                    inLabels = false;
                    continue;
                }

                if (inLabels && line.StartsWith("    - ", StringComparison.Ordinal))
                    Add(result, line.Substring("    - ".Length), address);
            }
        }

        return result;
    }

    private static Dictionary<string, HashSet<string>> ParseABLabelQueries()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string[] lines = RepoSource.ReadLines("Assets/FYAsset/CollectorData/CollectorSetting.asset");
        bool inEntries = false;
        bool inEntry = false;
        bool inLabels = false;
        string address = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line == "  AssetEntries:")
            {
                inEntries = true;
                continue;
            }

            if (!inEntries) continue;
            if (line.StartsWith("  - AssetGUID: ", StringComparison.Ordinal))
            {
                inEntry = true;
                inLabels = false;
                address = null;
                continue;
            }

            if (!inEntry) continue;
            if (line.StartsWith("    Address: ", StringComparison.Ordinal))
            {
                address = line.Substring("    Address: ".Length);
                if (AbE2EFixtureAddresses.Contains(address))
                    address = null;
                continue;
            }

            if (line == "    Labels:")
            {
                inLabels = true;
                continue;
            }

            if (line.StartsWith("    Labels: []", StringComparison.Ordinal))
            {
                inLabels = false;
                continue;
            }

            if (inLabels && line.StartsWith("    - ", StringComparison.Ordinal))
            {
                Add(result, line.Substring("    - ".Length), address);
                continue;
            }

            if (inLabels && line.StartsWith("    ", StringComparison.Ordinal) &&
                !line.StartsWith("    - ", StringComparison.Ordinal))
            {
                inLabels = false;
            }
        }

        string luaIndexTask = RepoSource.Read(
            "Assets/FYAsset/Scripts/Compat/Editor/Build/LuaScriptsIndexBuildTask.cs");
        RepoAssert.Contains(luaIndexTask, "Labels = new List<string> { LuaScriptsIndex.AssetAddress }",
            "AB LuaScriptsIndex publication must keep the LuaScriptsIndex label");
        Add(result, "LuaScriptsIndex", "LuaScriptsIndex");
        return result;
    }

    private static void Add(Dictionary<string, HashSet<string>> values, string label, string address)
    {
        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(address)) return;
        if (!values.TryGetValue(label, out HashSet<string> addresses))
        {
            addresses = new HashSet<string>(StringComparer.Ordinal);
            values[label] = addresses;
        }

        addresses.Add(address);
    }
}
