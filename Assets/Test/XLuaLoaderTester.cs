using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using XLua;

public class XLuaLoaderTester : MonoBehaviour
{
    public enum TestMode
    {
        EditorOnly,
        AddressablesOnly,
        Hybrid
    }

    public TestMode testMode = TestMode.Hybrid;
    public string luaModuleName = "HelloWorld";
    public List<string> editorRoots = new() { "Test/LuaScripts" };
    public List<string> aaLabels = new() { "LuaScripts" };
    public bool enableLog = true;

    private LuaEnv _luaEnv;

    private async void Start()
    {
        await TestXLuaLoaderAsync();
    }

    private void OnDestroy()
    {
        DisposeLuaEnv();
    }

    [ContextMenu("Test XLuaLoader")]
    public void TestXLuaLoader()
    {
        _ = TestXLuaLoaderAsync();
    }

    private async Task TestXLuaLoaderAsync()
    {
        DisposeLuaEnv();
        _luaEnv = new LuaEnv();

        var options = new XLuaLoader.Options
        {
            mode = (XLuaLoader.Mode)testMode,
            editorRoots = NormalizeEditorRoots(editorRoots)
        };

        if (aaLabels != null && aaLabels.Count > 0)
            options.ContainersAALabels.Add(aaLabels.ToArray());

        try
        {
            await XLuaLoader.SetupAndRegister(_luaEnv, options);
            _luaEnv.DoString($"require '{luaModuleName}'");
            if (enableLog)
                Debug.Log($"[XLuaLoaderTester] Loaded Lua module: {luaModuleName}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[XLuaLoaderTester] Lua execution failed: {ex.Message}");
        }
    }

    private void DisposeLuaEnv()
    {
        if (_luaEnv == null)
            return;

        _luaEnv.Dispose();
        _luaEnv = null;
    }

    private static List<string> NormalizeEditorRoots(IReadOnlyList<string> roots)
    {
        var result = new List<string>();
        if (roots == null || roots.Count == 0)
        {
            result.Add("Test/LuaScripts");
            return result;
        }

        for (int i = 0; i < roots.Count; i++)
        {
            string root = roots[i];
            if (string.IsNullOrWhiteSpace(root))
                continue;

            root = root.Trim().Replace('\\', '/').Trim('/');
            if (root.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                root = root.Substring("Assets/".Length);
            if (string.Equals(root, "AboutXLua/Test/LuaScripts", StringComparison.OrdinalIgnoreCase))
                root = "Test/LuaScripts";

            result.Add(root);
        }

        if (result.Count == 0)
            result.Add("Test/LuaScripts");
        return result;
    }
}
