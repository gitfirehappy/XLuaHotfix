using System;
using System.Collections.Generic;
using UObject = UnityEngine.Object;

/// <summary>
/// 句柄所有权契约门禁。
/// 验证每次 Load 或 Retain 对应独立 token，重复 Release 幂等；仍有活跃句柄时 Shutdown/Reset 必须拒绝。
/// </summary>
/// <remarks>
/// 通过 Registry 分配句柄模拟后端加载，直接验证生产 AssetHandle 与 HandleRegistry。
/// </remarks>
internal static class HandleTokenBehaviorTests
{
    private static readonly List<AssetHandle<UObject>> _tracked = new();
    private static int _releaseCallbackCount;
    private static readonly List<string> _releasedEntryIds = new();

    public static void Run()
    {
        GateChecks.RunAll(
            ("SeparateLoadsOwnSeparateTokens", VerifySeparateLoadsOwnSeparateTokens),
            ("RetainIsIndependentOwner", VerifyRetainIsIndependentOwner),
            ("DuplicateReleaseIsIdempotent", VerifyDuplicateReleaseIsIdempotent),
            ("DefaultHandleIsInvalid", VerifyDefaultHandleIsInvalid),
            ("ShutdownRefusesActiveHandles", VerifyShutdownRefusesActiveHandles));
    }

    /// <summary>同一 EntryId 的两次独立加载各自持有 token，释放一个不影响另一个。</summary>
    private static void VerifySeparateLoadsOwnSeparateTokens()
    {
        Reset();

        AssetHandle<UObject> first = Load("entry.a");
        AssetHandle<UObject> second = Load("entry.a");

        GateAssert.True(first.IsValid, "第一次加载的句柄必须有效");
        GateAssert.True(second.IsValid, "同一 EntryId 的第二次加载必须持有独立句柄");
        GateAssert.Equal(2, HandleRegistry.ActiveCount, "两个独立加载应占用两个活跃句柄");

        first.Release();
        GateAssert.True(second.IsValid, "释放其中一个句柄不得影响另一个句柄");
        GateAssert.Equal(0, _releaseCallbackCount, "同一 EntryId 仍有活跃句柄时不得触发底层释放回调");

        second.Release();
        GateAssert.False(second.IsValid, "最后一个句柄释放后必须失效");
        GateAssert.Equal(1, _releaseCallbackCount, "最后一个句柄释放后才触发一次底层释放回调");
    }

    /// <summary>Retain 必须分配独立 token；释放原句柄不得使 Retain 出来的句柄失效。</summary>
    private static void VerifyRetainIsIndependentOwner()
    {
        Reset();

        AssetHandle<UObject> original = Load("entry.b");
        AssetHandle<UObject> shared = original.Retain();
        _tracked.Add(shared);

        GateAssert.True(shared.IsValid, "Retain 返回的句柄必须有效");

        original.Release();
        GateAssert.False(original.IsValid, "释放原 token 后原句柄必须失效，不能与共享 token 共用同一槽位");
        GateAssert.True(shared.IsValid, "Retain 出来的 token 必须在原 token 释放后继续有效");
        GateAssert.Equal(0, _releaseCallbackCount, "仍持有 Retain token 时不得触发底层释放回调");

        shared.Release();
        GateAssert.Equal(1, _releaseCallbackCount, "最后一个 token 释放后才触发底层释放回调");
    }

    /// <summary>重复 Release 必须无效果，不得消耗其他 token 的释放权。</summary>
    private static void VerifyDuplicateReleaseIsIdempotent()
    {
        Reset();

        AssetHandle<UObject> original = Load("entry.c");
        AssetHandle<UObject> shared = original.Retain();
        _tracked.Add(shared);

        original.Release();
        original.Release();

        GateAssert.True(shared.IsValid, "重复 Release 不得消耗 Retain token 的释放权");
        GateAssert.Equal(0, _releaseCallbackCount, "重复 Release 不得触发底层释放回调");

        shared.Release();
        GateAssert.Equal(1, _releaseCallbackCount, "共享 token 释放后才触发一次底层释放回调");
    }

    /// <summary>default 句柄不指向任何真实资源，必须无效。</summary>
    private static void VerifyDefaultHandleIsInvalid()
    {
        Reset();

        AssetHandle<UObject> live = Load("entry.d");
        GateAssert.True(live.IsValid, "前置条件：已有一个活跃句柄占用初始槽位");

        AssetHandle<UObject> uninitialized = default;
        GateAssert.False(uninitialized.IsValid, "default 句柄不得因为槽位 0 恰好存活而被判定为有效");
        GateAssert.True(uninitialized.Asset == null, "default 句柄不得返回资源实例");
        uninitialized.Release();

        GateAssert.True(live.IsValid, "对 default 句柄调用 Release 不得影响其他句柄");
        GateAssert.Equal(0, _releaseCallbackCount, "对 default 句柄调用 Release 不得触发底层释放回调");
    }

    /// <summary>仍有活跃句柄时，关闭/重置必须拒绝，而不是静默清空计数。</summary>
    private static void VerifyShutdownRefusesActiveHandles()
    {
        Reset();

        AssetHandle<UObject> live = Load("entry.e");

        HandleRegistry.Reset();

        GateAssert.Equal(1, HandleRegistry.ActiveCount, "仍有活跃句柄时 Reset 必须拒绝，不得静默清空活跃计数");
        GateAssert.True(live.IsValid, "被拒绝的 Reset 不得使既有句柄失效");
        GateAssert.Equal(0, _releaseCallbackCount, "Reset 拒绝路径不得触发底层释放回调");

        live.Release();
        GateAssert.Equal(1, _releaseCallbackCount, "释放最后一个句柄后必须触发底层释放回调");

        HandleRegistry.Reset();
        GateAssert.Equal(0, HandleRegistry.ActiveCount, "无活跃句柄时 Reset 必须成功清空");
    }

    /// <summary>
    /// 生产分配入口适配点：HandleRegistry 一个 token 一个槽位，
    /// 这里按 Asset 类别分配 token 并构造句柄，模拟一次成功的加载。
    /// </summary>
    private static AssetHandle<UObject> Load(string entryId)
    {
        (int tokenId, int generation) = HandleRegistry.Alloc(
            entryId,
            HandleKind.Asset,
            null,
            OnReleased);
        var handle = new AssetHandle<UObject>(tokenId, generation, new UObject { name = entryId });
        _tracked.Add(handle);
        return handle;
    }

    private static void OnReleased(string entryId)
    {
        _releaseCallbackCount++;
        _releasedEntryIds.Add(entryId);
    }

    /// <summary>清空前置状态：先释放本门禁创建过的全部句柄，再重置 Registry。</summary>
    private static void Reset()
    {
        for (int i = 0; i < _tracked.Count; i++)
        {
            if (_tracked[i].IsValid)
                _tracked[i].Release();
        }

        _tracked.Clear();
        _releaseCallbackCount = 0;
        _releasedEntryIds.Clear();

        HandleRegistry.Reset();
        GateAssert.Equal(0, HandleRegistry.ActiveCount, "门禁前置条件：Reset 后活跃句柄必须为 0");
    }
}
