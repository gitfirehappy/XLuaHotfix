using System;
using System.Collections.Generic;

/// <summary>AB remediation 的最小纯逻辑合同：分析前置校验、Manifest 映射、系统来源和 Labels 契约。</summary>
internal static class ContentAnalysisValidationTests
{
    public static void Declare(GateRun run)
    {
        run.Check("ValidSerializedContentPasses", ValidSerializedContentPasses);
        run.Check("InvalidContentGroupsFailBeforeBuild", InvalidContentGroupsFailBeforeBuild);
        run.Check("ManifestRejectsAlteredBuildFact", ManifestRejectsAlteredBuildFact);
        run.Check("ResourcesIsSingleReadOnlySystemSource", ResourcesIsSingleReadOnlySystemSource);
        run.Check("LabelsAllowAbsentPreserveFirstAndRejectDuplicates", LabelsAllowAbsentPreserveFirstAndRejectDuplicates);
    }

    private static void ValidSerializedContentPasses()
    {
        BuildTaskResult result = ABContentAnalysisValidator.Validate(new List<CollectedAssetInfo>
        {
            Asset("content-a", "Assets/A.prefab", "GameObject", AssetContentType.SerializedObject),
            Asset("content-a", "Assets/B.prefab", "gameobject", AssetContentType.SerializedObject)
        });
        Check.True(result.Success, result.ErrorMessage ?? "合法 SerializedObject Content 应通过 Analyze 校验");
    }

    private static void InvalidContentGroupsFailBeforeBuild()
    {
        AssertFailure(BuildErrorCodes.MixedPayloadBundle, ABContentAnalysisValidator.Validate(new List<CollectedAssetInfo>
        {
            Asset("content-a", "Assets/A.prefab", "GameObject", AssetContentType.SerializedObject),
            Asset("content-a", "Assets/B.unity", "GameObject", AssetContentType.Scene)
        }));
        AssertFailure(BuildErrorCodes.MixedAssetTypeBundle, ABContentAnalysisValidator.Validate(new List<CollectedAssetInfo>
        {
            Asset("content-a", "Assets/A.asset", "Material", AssetContentType.SerializedObject),
            Asset("content-a", "Assets/B.asset", "Texture2D", AssetContentType.SerializedObject)
        }));
        AssertFailure(BuildErrorCodes.InvalidBundleEntryAsset, ABContentAnalysisValidator.Validate(new List<CollectedAssetInfo>
        {
            Asset("content-a", "Assets/Invalid.asset", "DefaultAsset", AssetContentType.SerializedObject)
        }));
        AssertFailure(BuildErrorCodes.RawfileMultiAsset, ABContentAnalysisValidator.Validate(new List<CollectedAssetInfo>
        {
            Asset("raw-content", "Assets/A.bytes", "DefaultAsset", AssetContentType.RawFile),
            Asset("raw-content", "Assets/B.bytes", "DefaultAsset", AssetContentType.RawFile)
        }));
    }

    private static void ManifestRejectsAlteredBuildFact()
    {
        var manifest = new ABManifest
        {
            ContentEntries = new List<ManifestContentEntry>
            {
                new ManifestContentEntry
                {
                    FileName = "content-a", Hash = "hash-a", CRC = 17, Size = 128,
                    ContentType = AssetContentType.SerializedObject
                }
            }
        };
        var builds = new List<ContentBuildResult>
        {
            new ContentBuildResult
            {
                ContentName = "logical-a", FileName = "content-a", Hash = "hash-b", CRC = 17, Size = 128,
                ContentType = AssetContentType.SerializedObject
            }
        };
        var issues = new List<VerificationIssue>();
        int errors = 0;
        int warnings = 0;
        VerifyABContentTask.VerifyContentMapping(manifest, builds, issues, ref errors, ref warnings);
        Check.Equal(1, errors, "ContentBuildResult 被改变后必须由 Verify 拒绝，不能回读磁盘覆盖差异");
    }

    private static void ResourcesIsSingleReadOnlySystemSource()
    {
        var result = new ScanResult();
        Check.Equal(1, result.SystemSources.Count, "ScanResult 必须固定且仅暴露一个 Resources 系统来源");
        Check.Equal("Assets/Resources", result.SystemSources[0].RootPath, "Resources 系统来源根路径不匹配");
        Check.True(result.SystemSources[0].ReadOnly, "Resources 系统来源必须只读");
        Check.False(result.SystemSources is List<CollectionSystemSource>, "SystemSources 不得暴露可变 List");
        Check.Equal(0, result.Assets.Count, "固定 Resources 系统来源不得自动生成 CollectedAssetInfo");
    }

    private static void LabelsAllowAbsentPreserveFirstAndRejectDuplicates()
    {
        Check.True(AssetLabelValidator.TryValidate(null, out _, out _), "Labels 缺省必须允许");
        var labels = new List<string> { "Feature", "UI" };
        Check.True(AssetLabelValidator.TryValidate(labels, out _, out _), "合法 Labels 应通过");
        Check.Equal("Feature", labels[0], "校验不得改写首次填写的大小写");
        Check.False(AssetLabelValidator.TryValidate(new List<string> { "Feature", "feature" }, out _, out string duplicate),
            "大小写不同的重复 Labels 必须拒绝");
        Check.Equal("feature", duplicate, "重复诊断必须指向后出现的原始拼写");
    }

    private static CollectedAssetInfo Asset(string contentName, string assetPath, string assetType, AssetContentType contentType)
    {
        return new CollectedAssetInfo
        {
            ContentName = contentName,
            AssetPath = assetPath,
            AssetType = assetType,
            ContentType = contentType
        };
    }

    private static void AssertFailure(string expectedCode, BuildTaskResult result)
    {
        Check.False(result.Success, $"应在 Analyze 阶段失败，实际成功；期望错误码={expectedCode}");
        Check.Equal(expectedCode, result.ErrorCode, "Analyze 校验错误码不匹配");
    }
}
