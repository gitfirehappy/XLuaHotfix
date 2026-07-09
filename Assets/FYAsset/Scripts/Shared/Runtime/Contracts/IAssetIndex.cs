using System.Collections.Generic;

/// <summary>
/// 资源索引接口 — 条目级查询。
/// </summary>
public interface IAssetIndex
{
    RuntimeAssetEntry GetEntryById(string entryId);
    IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddress(string address);
    IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddressAndType(string address, string primaryType);
    IReadOnlyList<RuntimeAssetEntry> GetAllEntries();
}
