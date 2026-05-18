using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class VersionState
{
    [FormerlySerializedAs("version")]
    public VersionNumber Version;  // 版本号,可用于UI显示

    // Deprecated: 旧 version_state.json 中 "version" 字段的 JSON 反序列化桥接。
    // JsonUtility 大小写敏感，不识别 FormerlySerializedAs，因此保留此私有字段接收旧 JSON。
    [SerializeField] private VersionNumber version; 
    public string FileHash;     // 唯一比对标识
    public long TotalSize;  // 更新bundle的总大小
    public List<BundleInfo> Bundles = new(); // 导出的的bundle列表

    /// <summary>
    /// 旧 JSON 兼容：若 Version 为 null 且旧 "version" 字段有值，则迁移。
    /// 新 JSON 同时序列化 Version + version，反序列化后 Version 优先。
    /// </summary>
    public void MigrateLegacyVersionField()
    {
        if (Version == null && version != null)
        {
            Version = version;
            version = null;
        }
    }
}

[Serializable]
public class BundleInfo
{
    public string BundleName;   // bundle 文件名（e.g group_assets_label_hash.bundle）
    public string FileHash;         // bundle 文件的 hash
    // TODO: Legacy version_state.json files do not contain FileCRC and deserialize to 0.
    // Keep 0 as "skip CRC verification" until the VersionState unification decision is executed.
    public uint FileCRC;            // bundle 文件的 CRC32 快速校验码
    public long FileSize;           // bundle 文件大小
}
