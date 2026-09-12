/// <summary>
/// 指向最新远端包或本地最近成功激活的包，包含后端身份和版本。
/// 不包含资产与 Bundle 清单，实际路径由运行时或发布流程组合。
/// </summary>
[System.Serializable]
public class PackageIndex
{
    public string LatestPackage;
    public VersionNumber LatestVersion;
    public string BackendMode;
}
