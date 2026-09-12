using System;

namespace UnityEngine
{
    public class Object
    {
        public string name;
    }

    public sealed class TextAsset : Object
    {
        public TextAsset(string text = "")
        {
            this.text = text;
        }

        public string text;
    }

    public static class Application
    {
        public static string streamingAssetsPath = "streaming";
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
    }

    public class AsyncOperation
    {
        private bool _isDone;

        public bool isDone => _isDone;
        public event Action<AsyncOperation> completed;

        internal void CompleteOperation()
        {
            if (_isDone) return;
            _isDone = true;
            Action<AsyncOperation> callback = completed;
            callback?.Invoke(this);
        }
    }

    public sealed class AssetBundleCreateRequest : AsyncOperation
    {
        internal string Path;
        internal AssetBundle LoadedBundle;

        public AssetBundle assetBundle
        {
            get
            {
                if (!isDone) FakeAssetBundleIO.ForceComplete(this);
                return LoadedBundle;
            }
        }

        internal void Complete(AssetBundle bundle)
        {
            LoadedBundle = bundle;
            CompleteOperation();
        }
    }

    public sealed class AssetBundleRequest : AsyncOperation
    {
        internal Object RequestedAsset;

        public Object asset => RequestedAsset;
    }

    public sealed class AssetBundle : Object
    {
        internal string Path;
        internal bool IsUnloaded;

        public static AssetBundle LoadFromFile(string path)
        {
            return FakeAssetBundleIO.LoadFromFile(path);
        }

        public static AssetBundleCreateRequest LoadFromFileAsync(string path)
        {
            return FakeAssetBundleIO.LoadFromFileAsync(path);
        }

        /// <summary>资产提取替身：返回一个新的 Object，调用方按类型断言所属 Bundle。</summary>
        public T LoadAsset<T>(string name) where T : Object
        {
            return (T)(object)new Object { name = name };
        }

        /// <summary>异步提取替身：请求立即完成，行为与同步兼容。</summary>
        public AssetBundleRequest LoadAssetAsync<T>(string name) where T : Object
        {
            var request = new AssetBundleRequest { RequestedAsset = new Object { name = name } };
            request.CompleteOperation();
            return request;
        }

        public void Unload(bool unloadAllLoadedObjects)
        {
            if (IsUnloaded) return;
            IsUnloaded = true;
            FakeAssetBundleIO.RecordUnload(Path);
        }
    }
}

namespace UnityEngine.Networking
{
}
