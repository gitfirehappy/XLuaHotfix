/// <summary>
/// 包目录与发布工作区使用到的固定文件名。
/// </summary>
/// <remarks>
/// 集中声明的目的：发布流程与本地目录目标必须使用同一份名字事实。
/// 包目录只含发布内容，因此这里只保留发布工作区自身的固定名字。本类型不依赖 UnityEngine。
/// </remarks>
public static class PackageFileNames
{
    /// <summary>发布事务在服务器根下的隔离工作目录名</summary>
    public const string PushWorkFolderName = ".fyasset_push";
}
