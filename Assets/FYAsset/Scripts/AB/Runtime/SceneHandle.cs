using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景卸载出口。由场景加载器实现，供 SceneHandle.UnloadAsync 在消费 token 后调用。
/// </summary>
/// <remarks>
/// 与 AssetHandle 不同，Scene 槽位不携带 HandleRegistry 释放回调：
/// 场景内容的引用必须等场景真正卸载完成后才能释放，不能由 token 释放时机决定。
/// </remarks>
internal interface IABSceneUnloadSink
{
    /// <summary>卸载场景并释放其内容引用。返回 null 表示成功。</summary>
    Task<RuntimeMessage> UnloadSceneAsync(Scene scene, string scenePath);
}

/// <summary>
/// 场景卸载结果。失败不消费 token：调用方可以修正外部条件后重试同一个句柄。
/// </summary>
public readonly struct SceneUnloadResult
{
    /// <summary>是否完成物理卸载并结算 token</summary>
    public bool Success { get; }

    /// <summary>物理卸载失败原因；成功、句柄无效或被拒绝时为 null</summary>
    public RuntimeMessage Error { get; }

    /// <summary>被拒绝时该场景仍存活的所有者数量；其它情况为 0</summary>
    public int ActiveOwners { get; }

    private SceneUnloadResult(bool success, RuntimeMessage error, int activeOwners)
    {
        Success = success;
        Error = error;
        ActiveOwners = activeOwners;
    }

    internal static SceneUnloadResult Ok => new SceneUnloadResult(true, null, 0);

    internal static SceneUnloadResult Invalid => new SceneUnloadResult(false, null, 0);

    internal static SceneUnloadResult Busy(int activeOwners) => new SceneUnloadResult(false, null, activeOwners);

    internal static SceneUnloadResult Failed(RuntimeMessage error) => new SceneUnloadResult(false, error, 0);
}

/// <summary>
/// 场景句柄值，持有一个 HandleRegistry Scene token 的快照、场景身份与卸载出口。
/// </summary>
/// <remarks>
/// 所有权语义与 AssetHandle{T} 一致：一次 LoadScene 或一次 Retain 对应一个独立 token，
/// 普通 struct 复制不增加所有权，重复 Release 幂等，default(SceneHandle) 永远无效。
/// Additive 场景由调用方在 UnloadAsync 中卸载；Single 模式由加载器在新场景激活并确认旧场景卸载后结算旧句柄。
/// </remarks>
public struct SceneHandle
{

    /// <summary>Registry tokenId。0 表示 default 或失败句柄，不占用槽位。</summary>
    internal int HandleId;

    /// <summary>创建时的 Generation 快照。</summary>
    internal int Generation;

    /// <summary>加载时使用的公共 Address（诊断用）</summary>
    private string _address;

    /// <summary>AssetBundle.GetAllScenePaths() 给出的完整场景路径</summary>
    private string _scenePath;

    /// <summary>已加载的场景；未激活或失败时为 default</summary>
    private Scene _scene;

    /// <summary>失败句柄的内联错误（HandleId=0 时使用）</summary>
    private RuntimeMessage _inlineError;

    /// <summary>卸载出口。仅成功句柄携带，复制体共享同一个出口。</summary>
    private IABSceneUnloadSink _sink;

    /// <summary>
    /// 成功构造：关联 HandleRegistry Scene 槽位。
    /// </summary>
    internal SceneHandle(
        int handleId,
        int generation,
        string address,
        string scenePath,
        Scene scene,
        IABSceneUnloadSink sink)
    {
        HandleId = handleId;
        Generation = generation;
        _address = address;
        _scenePath = scenePath;
        _scene = scene;
        _inlineError = null;
        _sink = sink;
    }

    /// <summary>
    /// 失败构造：不占用 Registry 槽位，错误信息内联存储。
    /// </summary>
    internal SceneHandle(RuntimeMessage error, string address, string scenePath)
    {
        HandleId = 0;
        Generation = 0;
        _address = address;
        _scenePath = scenePath;
        _scene = default;
        _inlineError = error;
        _sink = null;
    }

    /// <summary>句柄是否仍然有效（token 未释放且未过期）。</summary>
    public bool IsValid => HandleId >= 1 && HandleRegistry.IsValid(HandleId, Generation);

    /// <summary>
    /// 结构化错误信息。失败句柄（含 default）返回内联错误；有效 token 返回 null。
    /// </summary>
    public RuntimeMessage Error => HandleId < 1 ? _inlineError : HandleRegistry.GetError(HandleId, Generation);

    /// <summary>请求的公共 Address（诊断用）</summary>
    public string Address => _address;

    /// <summary>完整场景路径；解析失败时为 null。</summary>
    public string ScenePath => _scenePath;

    /// <summary>已加载的场景。句柄无效时返回 default(Scene)。</summary>
    public Scene Scene => IsValid ? _scene : default;

    /// <summary>
    /// 为同一场景分配一个新的独立 token，并返回携带该 token 的新句柄。
    /// 失败句柄原样返回；default 句柄或已过期 token 返回 default(SceneHandle)。
    /// </summary>
    public SceneHandle Retain()
    {
        if (HandleId < 1)
            return _inlineError != null ? this : default;

        if (!HandleRegistry.Retain(HandleId, Generation, out int tokenId, out int generation))
            return default;

        return new SceneHandle(tokenId, generation, _address, _scenePath, _scene, _sink);
    }

    /// <summary>
    /// 消费当前 token 一次。default 句柄、失败句柄、过期或重复释放都是静默 no-op。
    /// </summary>
    public void Release()
    {
        if (HandleId < 1) return;
        HandleRegistry.Release(HandleId, Generation);
    }

    /// <summary>
    /// 卸载场景并释放其内容引用。只有该场景的最后一个所有者可以触发物理卸载。
    /// </summary>
    /// <remarks>
    /// 物理卸载成功后才结算自己的 token：卸载失败或仍有其它所有者时 token 保留，调用方可以重试。
    /// 句柄无效（default / 失败 / 已结算 / 已释放）时返回 Success=false，不产生任何副作用；
    /// 仍有其它所有者时返回 Success=false 且 ActiveOwners 给出活跃所有者数量，不调用物理卸载。
    /// </remarks>
    public Task<SceneUnloadResult> UnloadAsync()
    {
        if (!IsValid) return Task.FromResult(SceneUnloadResult.Invalid);

        int owners = HandleRegistry.GetActiveOwnerCount(HandleId, Generation);
        if (owners > 1) return Task.FromResult(SceneUnloadResult.Busy(owners));

        IABSceneUnloadSink sink = _sink;
        if (sink == null) return Task.FromResult(SceneUnloadResult.Invalid);

        return UnloadAsLastOwnerAsync(sink, _scene, _scenePath, HandleId, Generation);
    }

    private static async Task<SceneUnloadResult> UnloadAsLastOwnerAsync(
        IABSceneUnloadSink sink, Scene scene, string scenePath, int handleId, int generation)
    {
        RuntimeMessage error = await sink.UnloadSceneAsync(scene, scenePath);
        if (error != null)
        {
            if (error.Severity == RuntimeSeverity.Warning)
                Debug.LogWarning(error.ToString());
            else
                Debug.LogError(error.ToString());

            // 失败保留 token：场景状态未知，调用方必须能够再次尝试同一个句柄。
            return SceneUnloadResult.Failed(error);
        }

        // 物理卸载确认之后才结算 token，否则失败会把重试入口一起丢掉。
        HandleRegistry.Release(handleId, generation);
        return SceneUnloadResult.Ok;
    }

    public override string ToString()
    {
        if (IsValid)
            return string.Concat("[SceneHandle OK] token=", HandleId.ToString(),
                " gen=", Generation.ToString(), " path=", _scenePath ?? "");

        if (_inlineError != null)
            return string.Concat("[SceneHandle Failed] ", _inlineError.ToString());

        return string.Concat("[SceneHandle Invalid] token=", HandleId.ToString(),
            " gen=", Generation.ToString(), " path=", _scenePath ?? "");
    }

}
