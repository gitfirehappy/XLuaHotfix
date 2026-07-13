#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 控制仅限本机访问的 Python 服务，用于测试已发布的热更文件。
/// </summary>
[InitializeOnLoad]
public static class LocalHotfixServerController
{
    private const int DefaultPort = 18080;
    private const string PortEditorPrefsKey = "FYAsset.LocalHotfixServer.Port";
    private const string StateRelativePath = "Library/FYAsset/LocalHotfixServer.json";
    private const string ServerScriptRelativePath = "CommandLine/hotfix_server.py";

    static LocalHotfixServerController()
    {
        EditorApplication.quitting += StopOnEditorQuit;
    }

    public static int Port
    {
        get => EditorPrefs.GetInt(PortEditorPrefsKey, DefaultPort);
        set => EditorPrefs.SetInt(PortEditorPrefsKey, Mathf.Clamp(value, 1024, 65535));
    }

    public static string RootUrl => $"http://127.0.0.1:{Port}/";

    public static LocalHotfixServerStatus GetStatus()
    {
        LocalHotfixServerState state = LoadState();
        if (state == null)
            return LocalHotfixServerStatus.Stopped("Local server is stopped.");

        if (TryReadHealth(state, out string healthRoot))
            return LocalHotfixServerStatus.Running(state.ProcessId, state.Port, healthRoot);

        return LocalHotfixServerStatus.Stopped("Local server state is stale or unreachable.");
    }

    public static LocalHotfixServerStatus Start()
    {
        LocalHotfixServerStatus current = GetStatus();
        if (current.IsRunning)
            return current;

        FileHelper.TryDelete(GetStatePath());
        int port = Port;
        if (!IsPortAvailable(port))
            return LocalHotfixServerStatus.Stopped($"Port {port} is already in use.");

        PushTargetConfig localConfig = FindLocalTarget();
        if (localConfig == null)
            return LocalHotfixServerStatus.Stopped("No LocalDirectory push target is configured.");

        string root = PushTargetUtility.ResolveServiceRoot(localConfig);
        string scriptPath = FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, ServerScriptRelativePath);
        if (!FileHelper.Exists(scriptPath))
            return LocalHotfixServerStatus.Stopped($"Server script is missing: {scriptPath}");

        string pythonPath = PushTargetUtility.FindExecutableOnPath("python");
        if (string.IsNullOrEmpty(pythonPath))
            return LocalHotfixServerStatus.Stopped("Python was not found on PATH.");

        FileHelper.EnsureDirectory(root);
        string token = Guid.NewGuid().ToString("N");
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"{PushTargetUtility.QuoteArgument(scriptPath)} --root {PushTargetUtility.QuoteArgument(root)} --port {port} --token {PushTargetUtility.QuoteArgument(token)}",
            WorkingDirectory = BuildPathManager.ProjectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process == null)
                return LocalHotfixServerStatus.Stopped("Python server process did not start.");

            var state = new LocalHotfixServerState
            {
                ProcessId = process.Id,
                Port = port,
                Root = root,
                Token = token
            };
            SaveState(state);

            for (int i = 0; i < 30; i++)
            {
                if (TryReadHealth(state, out string healthRoot))
                    return LocalHotfixServerStatus.Running(process.Id, port, healthRoot);
                Thread.Sleep(100);
            }

            if (!process.HasExited)
                process.Kill();
            FileHelper.TryDelete(GetStatePath());
            return LocalHotfixServerStatus.Stopped("Python server did not become healthy.");
        }
        catch (Exception ex)
        {
            if (process != null && !process.HasExited)
                process.Kill();
            FileHelper.TryDelete(GetStatePath());
            Debug.LogError($"[LocalHotfixServerController] 启动失败：{ex}");
            return LocalHotfixServerStatus.Stopped(ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    public static LocalHotfixServerStatus Stop()
    {
        LocalHotfixServerState state = LoadState();
        if (state == null)
            return LocalHotfixServerStatus.Stopped("Local server is already stopped.");

        if (!TryReadHealth(state, out _))
            return LocalHotfixServerStatus.Stopped("Server identity could not be verified; no process was terminated.");

        try
        {
            string url = $"http://127.0.0.1:{state.Port}/__fyasset_shutdown?token={Uri.EscapeDataString(state.Token)}";
            using WebResponse response = CreateRequest(url).GetResponse();
            FileHelper.TryDelete(GetStatePath());
            return LocalHotfixServerStatus.Stopped("Local server stopped.");
        }
        catch (Exception ex)
        {
            return LocalHotfixServerStatus.Stopped($"Server stop failed: {ex.Message}");
        }
    }

    private static PushTargetConfig FindLocalTarget()
    {
        PushTargetConfig named = PushTargetUtility.FindConfig("local");
        if (named != null && named.Type == PushTargetType.LocalDirectory)
            return named;

        FYAssetSettings settings = FYAssetSettings.Instance;
        for (int i = 0; settings.PushTargets != null && i < settings.PushTargets.Count; i++)
        {
            PushTargetConfig config = settings.PushTargets[i];
            if (config != null && config.Type == PushTargetType.LocalDirectory)
                return config;
        }

        return null;
    }

    private static bool TryReadHealth(LocalHotfixServerState state, out string root)
    {
        root = string.Empty;
        if (state == null || string.IsNullOrEmpty(state.Token) || state.Port <= 0)
            return false;

        try
        {
            string url = $"http://127.0.0.1:{state.Port}/__fyasset_health";
            using WebResponse response = CreateRequest(url).GetResponse();
            using var reader = new StreamReader(response.GetResponseStream());
            LocalHotfixServerHealth health = JsonUtility.FromJson<LocalHotfixServerHealth>(reader.ReadToEnd());
            if (health == null || !health.ok || !string.Equals(health.token, state.Token, StringComparison.Ordinal))
                return false;
            root = health.root;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static HttpWebRequest CreateRequest(string url)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Proxy = null;
        request.Timeout = 700;
        request.ReadWriteTimeout = 700;
        request.KeepAlive = false;
        return request;
    }

    private static bool IsPortAvailable(int port)
    {
        TcpListener listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }

    private static void StopOnEditorQuit()
    {
        if (!FileHelper.Exists(GetStatePath()))
            return;

        LocalHotfixServerStatus status = Stop();
        if (!string.IsNullOrEmpty(status.Message))
            Debug.Log($"[LocalHotfixServerController] {status.Message}");
    }

    private static string GetStatePath()
    {
        return FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, StateRelativePath);
    }

    private static LocalHotfixServerState LoadState()
    {
        string path = GetStatePath();
        if (!FileHelper.Exists(path))
            return null;
        try
        {
            return JsonUtility.FromJson<LocalHotfixServerState>(FileHelper.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LocalHotfixServerController] 状态读取失败：{ex.Message}");
            return null;
        }
    }

    private static void SaveState(LocalHotfixServerState state)
    {
        FileHelper.WriteAllTextAtomic(GetStatePath(), JsonUtility.ToJson(state, true));
    }

    [Serializable]
    private sealed class LocalHotfixServerState
    {
        public int ProcessId;
        public int Port;
        public string Root;
        public string Token;
    }

    [Serializable]
    private sealed class LocalHotfixServerHealth
    {
        public bool ok;
        public string token;
        public string root;
    }
}

public readonly struct LocalHotfixServerStatus
{
    public bool IsRunning { get; }
    public int ProcessId { get; }
    public int Port { get; }
    public string Root { get; }
    public string Message { get; }

    private LocalHotfixServerStatus(bool isRunning, int processId, int port, string root, string message)
    {
        IsRunning = isRunning;
        ProcessId = processId;
        Port = port;
        Root = root;
        Message = message;
    }

    public static LocalHotfixServerStatus Running(int processId, int port, string root) =>
        new(true, processId, port, root, $"Running at http://127.0.0.1:{port}/");

    public static LocalHotfixServerStatus Stopped(string message) =>
        new(false, 0, 0, string.Empty, message);
}
#endif
