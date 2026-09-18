using System;
using System.Threading.Tasks;

/// <summary>RawFile 读取器只消费物理路径，不持有 Address 或包根策略。</summary>
internal static class RawFileReaderTests
{
    public static async Task ReadsResolvedPhysicalPathOnly()
    {
        FileHelper.LastReadPath = null;
        FileHelper.RawFileBytes = new byte[] { 1, 2, 3 };

        byte[] bytes = await ABRawFileReader.ReadBytesAsync("/controlled/package/bundles/raw.bin");

        ScenarioAssert.Equal("/controlled/package/bundles/raw.bin", FileHelper.LastReadPath,
            "ABRawFileReader 必须把调用方提供的物理路径原样交给文件读取边界");
        ScenarioAssert.Equal(3, bytes.Length, "ABRawFileReader 必须原样返回读取到的字节");
    }
}
