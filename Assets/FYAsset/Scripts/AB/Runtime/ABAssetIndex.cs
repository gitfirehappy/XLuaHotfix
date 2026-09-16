using System;
using System.Collections.Generic;

/// <summary>
/// AB 公共资源索引。只索引 ManifestAssetEntry；Address 在单包内唯一。
/// </summary>
public sealed class ABAssetIndex
{
    private readonly ABManifest _manifest;
    private ManifestAssetEntry[] _entries;
    private Dictionary<string, ManifestAssetEntry> _addressIndex;
    private Dictionary<string, ManifestAssetEntry[]> _typeResults;
    private Dictionary<string, string[]> _typeAddressResults;
    private Dictionary<string, ManifestAssetEntry[]> _labelResults;
    private Dictionary<string, string[]> _labelAddressResults;

    public ABAssetIndex(ABManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        BuildIndex();
    }

    public RuntimeMessage BuildError { get; private set; }
    public bool IsValid => BuildError == null;

    private void BuildIndex()
    {
        List<ManifestAssetEntry> source = _manifest.AssetEntries ?? new List<ManifestAssetEntry>();
        int count = source.Count;
        _entries = source.ToArray();
        _addressIndex = new Dictionary<string, ManifestAssetEntry>(count, StringComparer.OrdinalIgnoreCase);
        var typeGroups = new Dictionary<string, List<ManifestAssetEntry>>(StringComparer.OrdinalIgnoreCase);
        var labelGroups = new Dictionary<string, List<ManifestAssetEntry>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < count; i++)
        {
            ManifestAssetEntry entry = _entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.Address))
            {
                BuildError = RuntimeMessage.LoadFailed("ABManifest", "ManifestAssetEntry 缺少 Address");
                ResetIndexes();
                return;
            }

            if (!_addressIndex.TryAdd(entry.Address, entry))
            {
                BuildError = RuntimeMessage.DuplicateAddress(entry.Address, 1);
                ResetIndexes();
                return;
            }

            if (!string.IsNullOrWhiteSpace(entry.AssetType))
                AddGroup(typeGroups, entry.AssetType, entry);

            if (entry.Labels == null)
                continue;
            for (int labelIndex = 0; labelIndex < entry.Labels.Count; labelIndex++)
            {
                string label = entry.Labels[labelIndex];
                if (!string.IsNullOrWhiteSpace(label))
                    AddGroup(labelGroups, label, entry);
            }
        }

        _typeResults = ConvertGroups(typeGroups);
        _typeAddressResults = ConvertAddresses(typeGroups);
        _labelResults = ConvertGroups(labelGroups);
        _labelAddressResults = ConvertAddresses(labelGroups);
    }

    private void ResetIndexes()
    {
        _addressIndex?.Clear();
        _typeResults = new Dictionary<string, ManifestAssetEntry[]>(StringComparer.OrdinalIgnoreCase);
        _typeAddressResults = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        _labelResults = new Dictionary<string, ManifestAssetEntry[]>(StringComparer.OrdinalIgnoreCase);
        _labelAddressResults = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    }

    public bool ContainsAddress(string address)
        => BuildError == null && !string.IsNullOrWhiteSpace(address) && _addressIndex.ContainsKey(address);

    public ManifestAssetEntry GetEntryByAddress(string address)
    {
        if (BuildError != null || string.IsNullOrWhiteSpace(address))
            return null;
        return _addressIndex.TryGetValue(address, out ManifestAssetEntry entry) ? entry : null;
    }

    public IReadOnlyList<ManifestAssetEntry> GetEntriesByType(string assetType)
    {
        if (BuildError != null || string.IsNullOrWhiteSpace(assetType))
            return Array.Empty<ManifestAssetEntry>();
        return _typeResults.TryGetValue(assetType, out ManifestAssetEntry[] entries)
            ? entries : Array.Empty<ManifestAssetEntry>();
    }

    public IReadOnlyList<ManifestAssetEntry> GetEntriesByLabel(string label)
    {
        if (BuildError != null || string.IsNullOrWhiteSpace(label))
            return Array.Empty<ManifestAssetEntry>();
        return _labelResults.TryGetValue(label, out ManifestAssetEntry[] entries)
            ? entries : Array.Empty<ManifestAssetEntry>();
    }

    public IReadOnlyList<string> GetAddressesByType(string assetType)
    {
        if (BuildError != null || string.IsNullOrWhiteSpace(assetType))
            return Array.Empty<string>();
        return _typeAddressResults.TryGetValue(assetType, out string[] addresses)
            ? addresses : Array.Empty<string>();
    }

    public IReadOnlyList<string> GetAddressesByLabel(string label)
    {
        if (BuildError != null || string.IsNullOrWhiteSpace(label))
            return Array.Empty<string>();
        return _labelAddressResults.TryGetValue(label, out string[] addresses)
            ? addresses : Array.Empty<string>();
    }

    internal ABManifest GetManifest() => _manifest;

    public IReadOnlyList<ManifestAssetEntry> GetAllEntries() => _entries;

    private static void AddGroup(Dictionary<string, List<ManifestAssetEntry>> groups, string key, ManifestAssetEntry entry)
    {
        if (!groups.TryGetValue(key, out List<ManifestAssetEntry> entries))
        {
            entries = new List<ManifestAssetEntry>();
            groups.Add(key, entries);
        }
        entries.Add(entry);
    }

    private static Dictionary<string, ManifestAssetEntry[]> ConvertGroups(Dictionary<string, List<ManifestAssetEntry>> groups)
    {
        var result = new Dictionary<string, ManifestAssetEntry[]>(groups.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in groups)
            result[pair.Key] = pair.Value.ToArray();
        return result;
    }

    private static Dictionary<string, string[]> ConvertAddresses(Dictionary<string, List<ManifestAssetEntry>> groups)
    {
        var result = new Dictionary<string, string[]>(groups.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in groups)
        {
            string[] addresses = new string[pair.Value.Count];
            for (int i = 0; i < pair.Value.Count; i++)
                addresses[i] = pair.Value[i].Address;
            result[pair.Key] = addresses;
        }
        return result;
    }
}
