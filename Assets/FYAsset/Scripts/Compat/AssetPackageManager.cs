using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 应用层资源加载 facade：启动时绑定一次 backend，之后统一转发 AA/AB。
/// </summary>
/// <remarks>
/// 对外保持 (asset, error) 与按 address 卸载的形状，内部为 AB 路径按 address 持有 Handle token 栈：
/// 每次加载压入一个 token，每次卸载弹出一个 token 并释放，最后一个 token 释放时才真正卸载内容。
/// 因此同 address 的重复加载必须与卸载一一配对，未配对时释放最后一个 token 才会生效。
/// </remarks>
public class AssetPackageManager : Singleton<AssetPackageManager>
{

    /// <summary>
    /// AB 句柄租约。只保留释放动作，避免按泛型类型保存句柄导致卸载时类型不匹配。
    /// </summary>
    private sealed class AssetLease
    {
        public Action Release;
    }

    // AB 公共 Address 契约是大小写不敏感：lease 身份必须与解析口径一致，否则按住地址无法释放。
    private readonly Dictionary<string, Stack<AssetLease>> _abLeases = new(StringComparer.OrdinalIgnoreCase);

    private BackendMode? _boundMode;

    public RuntimeMessage Bind(BackendMode mode)
    {
        if (mode != BackendMode.AA && mode != BackendMode.ABManifest)
            return RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, $"无效 backend mode: {mode}");

        if (!_boundMode.HasValue)
        {
            _boundMode = mode;
            Debug.Log($"[AssetPackageManager] 已绑定 backend: {BackendModeNames.FromBackendMode(mode)}");
            return null;
        }

        if (_boundMode.Value == mode)
            return null;

        return RuntimeMessage.Error(
            RuntimeErrorCodes.InvalidArgument,
            $"AssetPackageManager 已绑定 {_boundMode.Value}，不能切换为 {mode}");
    }

    public async Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string address)
        where T : UnityEngine.Object
    {
        if (!_boundMode.HasValue)
            return (null, RuntimeMessage.LoadFailed(address, "AssetPackageManager 尚未绑定 backend"));

        if (_boundMode.Value == BackendMode.AA)
            return await AAPackageManager.Instance.LoadAssetAsync<T>(address);

        AssetHandle<T> handle = await ABPackageManager.Instance.LoadByAddress<T>(address);
        return RetainLease(handle, address);
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string address)
        where T : UnityEngine.Object
    {
        if (!_boundMode.HasValue)
            return (null, RuntimeMessage.LoadFailed(address, "AssetPackageManager 尚未绑定 backend"));

        if (_boundMode.Value == BackendMode.AA)
            return AAPackageManager.Instance.LoadAssetSync<T>(address);

        AssetHandle<T> handle = ABPackageManager.Instance.LoadByAddressSync<T>(address);
        return RetainLease(handle, address);
    }

    public void UnloadAsset<T>(string address) where T : UnityEngine.Object
    {
        if (!_boundMode.HasValue)
        {
            Debug.LogError(RuntimeMessage.LoadFailed(address, "AssetPackageManager 尚未绑定 backend").ToString());
            return;
        }

        if (_boundMode.Value == BackendMode.AA)
        {
            AAPackageManager.Instance.UnloadAsset<T>(address);
            return;
        }

        ReleaseLease(address);
    }

    /// <summary>
    /// 记录一次成功加载的 token，并返回业务侧需要的 (asset, error)。
    /// 失败句柄不占用 token，直接返回错误，不进入栈。
    /// </summary>
    private (T asset, RuntimeMessage error) RetainLease<T>(AssetHandle<T> handle, string address)
        where T : UnityEngine.Object
    {
        if (!handle.IsValid)
            return (null, handle.Error ?? RuntimeMessage.LoadFailed(address, "AB 加载失败"));

        if (!_abLeases.TryGetValue(address, out Stack<AssetLease> leases))
        {
            leases = new Stack<AssetLease>();
            _abLeases[address] = leases;
        }

        leases.Push(new AssetLease { Release = handle.Release });
        return (handle.Asset, null);
    }

    /// <summary>
    /// 弹出最近一次加载的 token 并释放。栈为空表示加载与卸载未配对。
    /// </summary>
    private void ReleaseLease(string address)
    {
        if (!_abLeases.TryGetValue(address, out Stack<AssetLease> leases) || leases.Count == 0)
        {
            Debug.LogWarning(string.Concat(
                "[AssetPackageManager] 卸载时没有已加载的 AB 句柄: ", address ?? ""));
            return;
        }

        AssetLease lease = leases.Pop();
        if (leases.Count == 0)
            _abLeases.Remove(address);

        lease.Release?.Invoke();
    }
}
