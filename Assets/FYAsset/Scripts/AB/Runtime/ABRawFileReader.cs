using System;
using System.Threading.Tasks;

/// <summary>只读取调用方已解析的物理 RawFile 路径；不解析 Address、不选择包根、不缓存。</summary>
public static class ABRawFileReader
{
    public static Task<byte[]> ReadBytesAsync(string physicalPath)
    {
        if (string.IsNullOrEmpty(physicalPath))
            throw new ArgumentException("RawFile 物理路径为空。", nameof(physicalPath));
        return FileHelper.ReadAllBytesAsync(physicalPath);
    }
}
