using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

internal interface IABSceneUnloadSink
{
    Task<RuntimeMessage> UnloadSceneAsync(Scene scene, string scenePath);
}

public readonly struct SceneUnloadResult
{
    public bool Success { get; }
    public RuntimeMessage Error { get; }
    public int ActiveOwners { get; }
    private SceneUnloadResult(bool success, RuntimeMessage error, int activeOwners)
    {
        Success = success;
        Error = error;
        ActiveOwners = activeOwners;
    }
    internal static SceneUnloadResult Ok => new SceneUnloadResult(true, null, 0);
    internal static SceneUnloadResult Invalid => new SceneUnloadResult(false, null, 0);
    internal static SceneUnloadResult Busy(int owners) => new SceneUnloadResult(false, null, owners);
    internal static SceneUnloadResult Failed(RuntimeMessage error) => new SceneUnloadResult(false, error, 0);
}

/// <summary>场景所有权句柄；只有最后一个 Address owner 执行物理卸载。</summary>
public struct SceneHandle
{
    internal int HandleId;
    internal int Generation;
    private string _address;
    private string _scenePath;
    private Scene _scene;
    private RuntimeMessage _inlineError;
    private IABSceneUnloadSink _sink;

    internal SceneHandle(int handleId, int generation, string address, string scenePath, Scene scene, IABSceneUnloadSink sink)
    {
        HandleId = handleId;
        Generation = generation;
        _address = address;
        _scenePath = scenePath;
        _scene = scene;
        _inlineError = null;
        _sink = sink;
    }

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

    public bool IsValid => HandleId >= 1 && HandleRegistry.IsValid(HandleId, Generation);
    public RuntimeMessage Error => HandleId < 1 ? _inlineError : HandleRegistry.GetError(HandleId, Generation);
    public string Address => _address;
    public string ScenePath => _scenePath;
    public Scene Scene => IsValid ? _scene : default;

    public SceneHandle Retain()
    {
        if (HandleId < 1)
            return _inlineError != null ? this : default;
        if (!HandleRegistry.Retain(HandleId, Generation, out int tokenId, out int generation))
            return default;
        return new SceneHandle(tokenId, generation, _address, _scenePath, _scene, _sink);
    }

    /// <summary>释放场景所有权；非最后 owner 只消费自身 token，最后 owner 等待实际卸载。</summary>
    public Task<SceneUnloadResult> ReleaseAsync()
    {
        if (!IsValid)
            return Task.FromResult(SceneUnloadResult.Invalid);

        int owners = HandleRegistry.GetActiveOwnerCount(HandleId, Generation);
        if (owners > 1)
        {
            HandleRegistry.Release(HandleId, Generation);
            return Task.FromResult(SceneUnloadResult.Busy(owners));
        }
        if (_sink == null)
            return Task.FromResult(SceneUnloadResult.Invalid);
        return ReleaseLastOwnerAsync(_sink, _scene, _scenePath, HandleId, Generation);
    }

    private static async Task<SceneUnloadResult> ReleaseLastOwnerAsync(
        IABSceneUnloadSink sink, Scene scene, string scenePath, int handleId, int generation)
    {
        RuntimeMessage error = await sink.UnloadSceneAsync(scene, scenePath);
        if (error != null)
            return SceneUnloadResult.Failed(error);
        HandleRegistry.Release(handleId, generation);
        return SceneUnloadResult.Ok;
    }

    public override string ToString()
        => IsValid ? string.Concat("[SceneHandle OK] ", _address) :
            _inlineError != null ? string.Concat("[SceneHandle Failed] ", _inlineError) : "[SceneHandle Invalid]";
}
