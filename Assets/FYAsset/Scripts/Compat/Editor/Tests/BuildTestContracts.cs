#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// Build Test 共享契约：后端/模式/阶段/退出码与结果模型。
/// </summary>
public static class BuildTestExitCodes
{
    public const int Passed = 0;
    public const int InvalidUsage = 2;
    public const int PreconditionFailed = 3;
    public const int BuildFailed = 4;
    public const int DiskAcceptanceFailed = 5;
    public const int RestoreFailed = 6;
    public const int TargetPreflightFailed = 7;
    public const int PublishOrProbeFailed = 8;
    public const int RuntimeFailed = 9;
    public const int Interrupted = 130;
}

public enum BuildTestBackend
{
    AA = 0,
    AB = 1
}

public enum BuildTestMode
{
    Full = 0,
    Hotfix = 1,
    Chain = 2,
    Standalone = 3
}

public enum BuildTestStage
{
    Idle = 0,
    Preflight = 1,
    Snapshot = 2,
    PrepareProject = 3,
    BuildFull = 4,
    AcceptFull = 5,
    PublishFull = 6,
    ProbeFull = 7,
    MutateFixture = 8,
    BuildHotfix = 9,
    AcceptHotfix = 10,
    PublishHotfix = 11,
    ProbeHotfix = 12,
    Restore = 13,
    PersistResult = 14,
    RecoveryOnly = 15
}

[Serializable]
public sealed class BuildTestTargetSnapshot
{
    public string TargetId;
    public PushTargetType TargetType;
    public string ServiceRoot;
    public string BackendPublishRoot;
    public string PublicBaseUrl;
    public string RuntimeUrl;
    public string PackageIndexUrl;
    public bool RequiresExternalConfirm;
}

[Serializable]
public sealed class BuildTestTargetOutcome
{
    public string TargetId;
    public bool PublishSuccess;
    public bool ProbeSuccess;
    public bool RestoreSuccess;
    public string Failure;
    public string PublishedPackage;
    public string PublishedVersion;
}

[Serializable]
public sealed class BuildTestStageTiming
{
    public string Stage;
    public double Seconds;
}

[Serializable]
public sealed class BuildTestResult
{
    public bool Passed;
    public int ExitCode = BuildTestExitCodes.PreconditionFailed;
    public string Backend;
    public string Mode;
    public string RunId;
    public string RunRoot;
    public string FirstFailure;
    public string FailedStage;
    public string PackagePath;
    public string ManifestHash;
    public string RepositoryHead;
    public string RepositoryParent;
    public string PackageIndexIdentity;
    public string StreamingAssetsBaselineHash;
    public string FixturePhysicalArtifact;
    public int ArtifactCount;
    public long ArtifactBytes;
    public int TaskTotal;
    public string ExpectedVersion;
    public string ActualVersion;
    public List<BuildTestTargetSnapshot> TargetSnapshots = new();
    public List<BuildTestTargetOutcome> TargetOutcomes = new();
    public List<BuildTestStageTiming> Stages = new();
    public bool RestorationSucceeded;
    public bool RecoveryOnly;
}

[Serializable]
public sealed class BuildTestRecoveryRecord
{
    public string RunId;
    public string Backend;
    public string Mode;
    public string CreatedAtUtc;
    public bool Completed;
    public bool Restored;
    public string ProjectBackupRoot;
    public string TargetsBackupRoot;
    public List<string> OwnedProjectPaths = new();
    public List<BuildTestTargetSnapshot> Targets = new();
    public List<string> FixturePaths = new();
}

/// <summary>
/// 单次 Build Test 运行请求。
/// </summary>
public sealed class BuildTestRequest
{
    public BuildTestBackend Backend;
    public BuildTestMode Mode;
    public List<string> TargetIds = new();
    public List<string> ExternalConfirmIds = new();
    public string ResultRootOverride;
    public Action<BuildTestStage, string> Progress;
    public bool SkipRetentionCleanup;
}

public static class BuildTestConstants
{
    public const string GroupName = "FYAssetPipelineTest";
    public const string Folder = "Assets/Test/FYAssetPipeline";
    public const string SmokeAssetTypePath = Folder + "/FYAssetPipelineSmokeAsset.cs";
    public const string AsyncAssetPath = Folder + "/FYAssetPipelineAsync.asset";
    public const string SyncAssetPath = Folder + "/FYAssetPipelineSync.txt";
    public const string RawAssetPath = Folder + "/FYAssetPipelineRaw.fyraw";
    public const string LuaModulePath = Folder + "/FYAssetPipelineSmoke.lua";
    public const string LuaContainerPath = Folder + "/FYAssetPipelineLuaContainer.asset";

    public const string AddressAsync = "FYAssetPipelineAsync";
    public const string AddressSync = "FYAssetPipelineSync";
    public const string AddressLua = "FYAssetPipelineLua";
    public const string AddressRaw = "FYAssetPipelineRaw";
    public const string LuaModuleName = "FYAssetPipelineSmoke";

    public const string MarkerAsync = "fyasset-pipeline-async:v1";
    public const string MarkerSyncV1 = "fyasset-pipeline-sync:v1";
    public const string MarkerSyncV2 = "fyasset-pipeline-sync:v2";
    public const string MarkerRawV1 = "fyasset-pipeline-raw:v1";
    public const string MarkerRawV2 = "fyasset-pipeline-raw:v2";
    public const string MarkerLua = "fyasset-pipeline-lua:v1";

    public const string LabelSmokeAsset = "FYAssetPipelineSmokeAsset";
    public const string LabelTextAsset = "TextAsset";
    public const string LabelLuaContainer = "LuaScriptContainer";
    public const string LabelGroup = "FYAssetPipelineTest";
}
#endif
