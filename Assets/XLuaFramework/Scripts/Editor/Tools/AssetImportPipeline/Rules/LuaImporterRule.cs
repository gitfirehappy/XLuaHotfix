using System;
using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// Lua 导入后端接口。
/// </summary>
public interface ILuaImportBackend
{
    TextAsset Import(string assetPath);
}

/// <summary>
/// 默认 Lua 文本导入后端：将 .lua 文件按 UTF8 文本导入为 TextAsset。
/// </summary>
public sealed class LuaTextAssetImportBackend : ILuaImportBackend
{
    public TextAsset Import(string assetPath)
    {
        byte[] fileData = File.ReadAllBytes(assetPath);
        string textContent = Encoding.UTF8.GetString(fileData);
        return new TextAsset(textContent);
    }
}

/// <summary>
/// Lua 导入后端注册器。
/// </summary>
public static class LuaImportBackendRegistry
{
    public static ILuaImportBackend Backend { get; set; } = new LuaTextAssetImportBackend();
}

/// <summary>
/// ScriptedImporter：处理 .lua 文件导入。
/// 通过 ILuaImportBackend 实现导入后端可替换。
/// </summary>
[ScriptedImporter(1, "lua")]
public class LuaImporterRule : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        if (LuaImportBackendRegistry.Backend == null)
        {
            throw new InvalidOperationException("[LuaImporterRule] LuaImportBackendRegistry.Backend 不能为空。");
        }

        TextAsset textAsset = LuaImportBackendRegistry.Backend.Import(ctx.assetPath);
        if (textAsset == null)
        {
            throw new InvalidOperationException($"[LuaImporterRule] 导入后端返回空结果: {ctx.assetPath}");
        }

        ctx.AddObjectToAsset("main", textAsset);
        ctx.SetMainObject(textAsset);
    }
}
