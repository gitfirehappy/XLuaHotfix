using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// XLuaFramework 对外的资源加载服务口。宿主项目在启动链显式注入实现（LuaAssetRuntime.SetLoader）。
/// 错误以 string 报告，null 表示成功。
/// </summary>
public interface ILuaAssetLoader
{
    Task<(T asset, string error)> LoadAssetAsync<T>(string address) where T : Object;
    (T asset, string error) LoadAssetSync<T>(string address) where T : Object;
    void UnloadAsset<T>(string address) where T : Object;
}
