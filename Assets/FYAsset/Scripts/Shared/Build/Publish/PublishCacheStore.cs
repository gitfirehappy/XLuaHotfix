#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 发布缓存的本地独立存储：`BuildData/PublishCache/{AA|AB}/{TargetId}.json`。
/// </summary>
/// <remarks>
/// 生命周期：与构建事实（Summary/Index）和历史包字节完全分离，只是一次发布的提示数据：
/// 1. 只有发布成功后写入，写入失败只记录 Warning；
/// 2. 读取失败一律按“没有缓存”处理，不影响发布正确性；
/// 3. 不参与任何写包、写索引或删除旧包的决定。
/// </remarks>
public static class PublishCacheStore
{
    /// <summary>读取发布缓存；缺失或损坏返回 null。</summary>
    public static PublishCache Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !FileHelper.Exists(path))
            return null;

        try
        {
            var cache = SerializationUtility.ReadFromFile<PublishCache>(path);
            if (cache?.Files == null)
                return null;

            return cache;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{nameof(PublishCacheStore)}] 发布缓存读取失败，按无缓存处理: {path} — {ex.Message}");
            return null;
        }
    }

    /// <summary>把发布缓存写入独立文件；失败只记录 Warning。</summary>
    public static void Save(string path, PublishCache cache)
    {
        if (string.IsNullOrEmpty(path) || cache == null)
            return;

        try
        {
            FileHelper.WriteAllTextAtomic(path, SerializationUtility.SerializeToJson(cache, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{nameof(PublishCacheStore)}] 发布缓存写入失败（不影响发布结果）: {path} — {ex.Message}");
        }
    }

    /// <summary>
    /// 与服务器事实做双重检测：只报告差异，不改变发布决定。
    /// </summary>
    /// <param name="cached">本地缓存的文件集合</param>
    /// <param name="serverFacts">服务器 Manifest 声明的文件集合</param>
    /// <returns>差异描述；一致或无法比较时返回 null</returns>
    public static string DescribeMismatch(IReadOnlyList<FileDigest> cached, IReadOnlyList<FileDigest> serverFacts)
    {
        if (cached == null || serverFacts == null)
            return null;

        FileDiff diff = FileDiff.Compute(cached, serverFacts);
        if (diff.IsEmpty)
            return null;

        return $"本地发布缓存与服务器事实不一致（以服务器为准）: 新增={diff.Added.Count}, 修改={diff.Modified.Count}, "
               + $"未变={diff.Unchanged.Count}, 移除={diff.Removed.Count}";
    }

    /// <summary>删除发布缓存文件（维护入口使用）；文件不存在视为成功。</summary>
    public static bool Delete(string path)
    {
        return string.IsNullOrEmpty(path) || FileHelper.TryDelete(path);
    }

    /// <summary>存在性检查，供面板显示。</summary>
    public static bool Exists(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);
}
#endif
