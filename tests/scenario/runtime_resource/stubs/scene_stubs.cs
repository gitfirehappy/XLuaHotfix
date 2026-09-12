using System;

// UnityEngine.SceneManagement 的最小替身：SceneHandle 需要 Scene 值类型，
// 场景加载/卸载（SceneManager、AsyncOperation 队列）只在 Unity 实测中验证。
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string path;
        public bool isLoaded;

        public bool IsValid() => !string.IsNullOrEmpty(path);
    }

    public enum LoadSceneMode
    {
        Single = 0,
        Additive = 1
    }
}
