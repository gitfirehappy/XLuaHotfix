using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class LogViewerWindow : EditorWindow
{
    private Vector2 _scrollPosition;
    private List<LogEntry> _filteredLogs = new List<LogEntry>();
    private string _searchText = "";
    
    // 筛选选项
    private bool[] _scriptTypeFilters = { true, true }; // C#, Lua
    private bool[] _logLevelFilters;
    private bool[] _logLayerFilters;
    
    private GUIStyle _errorStyle;
    private GUIStyle _warningStyle;
    private GUIStyle _infoStyle;
    
    // 存储枚举值列表
    private List<LogLevel> _logLevelValues;
    private List<LogLayer> _logLayerValues;

    [MenuItem("XLua/调试/Log Viewer")]
    public static void ShowWindow()
    {
        GetWindow<LogViewerWindow>("Log Viewer");
    }

    private void OnEnable()
    {
        // 获取枚举值列表
        _logLevelValues = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToList();
        _logLayerValues = Enum.GetValues(typeof(LogLayer)).Cast<LogLayer>().ToList();
        
        // 初始化筛选器
        _logLevelFilters = new bool[_logLevelValues.Count];
        for (int i = 0; i < _logLevelFilters.Length; i++) _logLevelFilters[i] = true;
        
        _logLayerFilters = new bool[_logLayerValues.Count];
        for (int i = 0; i < _logLayerFilters.Length; i++) _logLayerFilters[i] = true;
        
        // 注册日志更新事件
        LogUtility.OnLogAdded += UpdateLogs;
        
        // 初始化样式（增加字体大小和自动换行）
        _errorStyle = new GUIStyle(EditorStyles.label) { 
            normal = { textColor = Color.red },
            fontSize = 12, // 增大字体
            wordWrap = true, // 启用自动换行
            padding = new RectOffset(2, 2, 2, 2) // 增加内边距
        };
        _warningStyle = new GUIStyle(EditorStyles.label) { 
            normal = { textColor = new Color(1f, 0.6f, 0f) },
            fontSize = 12,
            wordWrap = true,
            padding = new RectOffset(2, 2, 2, 2)
        };
        _infoStyle = new GUIStyle(EditorStyles.label) { 
            normal = { textColor = Color.white },
            fontSize = 12,
            wordWrap = true,
            padding = new RectOffset(2, 2, 2, 2)
        };
        
        UpdateLogs();
    }

    private void OnDisable()
    {
        LogUtility.OnLogAdded -= UpdateLogs;
    }

    private void UpdateLogs()
    {
        var allLogs = LogUtility.LogEntries;
        _filteredLogs = FilterLogs(allLogs);
        Repaint();
    }

    private List<LogEntry> FilterLogs(IEnumerable<LogEntry> logs)
    {
        return logs.Where(log => 
        {
            // 脚本类型筛选
            bool scriptTypeMatch = 
                (_scriptTypeFilters[0] && log.ScriptType == "CSharp") || 
                (_scriptTypeFilters[1] && log.ScriptType == "Lua");
            
            if (!scriptTypeMatch) return false;
            
            // 日志级别筛选
            int levelIndex = _logLevelValues.IndexOf(log.Level);
            if (levelIndex < 0 || !_logLevelFilters[levelIndex]) return false;
            
            // 日志层筛选
            int layerIndex = _logLayerValues.IndexOf(log.Layer);
            if (layerIndex < 0 || !_logLayerFilters[layerIndex]) return false;
            
            // 搜索文本筛选
            if (!string.IsNullOrEmpty(_searchText) && 
                log.Source.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0 &&
                log.Message.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
            
            return true;
        }).OrderByDescending(log => log.Time).ToList();
    }

    private void OnGUI()
    {
        DrawToolbar();
        
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        
        // 增加日志条目之间的间距
        var logSpacing = 6;
        
        foreach (var log in _filteredLogs)
        {
            GUIStyle style = log.Level switch
            {
                LogLevel.Error => _errorStyle,
                LogLevel.Warning => _warningStyle,
                _ => _infoStyle
            };
            
            // 用Box包裹每条日志，增加边框区分
            EditorGUILayout.BeginVertical(EditorStyles.boldLabel);
            {
                // 日志内容（时间+消息）
                EditorGUILayout.LabelField(
                    $"{log.Time:HH:mm:ss} {log.FormattedMessage}", 
                    style, 
                    GUILayout.ExpandWidth(true)
                );
            }
            EditorGUILayout.EndVertical();
        
            // 添加日志之间的间隔
            EditorGUILayout.Space(logSpacing);
        }
        
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        // 脚本类型筛选
        EditorGUILayout.LabelField("脚本类型:", GUILayout.Width(70));
        bool csharpChanged = GUILayout.Toggle(_scriptTypeFilters[0], "CSharp", EditorStyles.toolbarButton, GUILayout.Width(60));
        bool luaChanged = GUILayout.Toggle(_scriptTypeFilters[1], "Lua", EditorStyles.toolbarButton, GUILayout.Width(40));
        
        // 日志级别筛选
        EditorGUILayout.LabelField("日志级别:", GUILayout.Width(70));
        bool levelChanged = false;
        for (int i = 0; i < _logLevelValues.Count; i++)
        {
            bool newValue = GUILayout.Toggle(_logLevelFilters[i], _logLevelValues[i].ToString(), EditorStyles.toolbarButton);
            if (newValue != _logLevelFilters[i])
            {
                _logLevelFilters[i] = newValue;
                levelChanged = true;
            }
        }
        
        // 日志层筛选
        EditorGUILayout.LabelField("日志层:", GUILayout.Width(60));
        bool layerChanged = false;
        for (int i = 0; i < _logLayerValues.Count; i++)
        {
            bool newValue = GUILayout.Toggle(_logLayerFilters[i], _logLayerValues[i].ToString(), EditorStyles.toolbarButton);
            if (newValue != _logLayerFilters[i])
            {
                _logLayerFilters[i] = newValue;
                layerChanged = true;
            }
        }
        
        // 搜索
        EditorGUILayout.LabelField("搜索:", GUILayout.Width(40));
        string newSearch = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarTextField, GUILayout.MinWidth(100));
        bool searchChanged = newSearch != _searchText;
        if (searchChanged)
        {
            _searchText = newSearch;
        }
        
        GUILayout.FlexibleSpace();
        
        // 清除日志按钮
        if (GUILayout.Button("清除日志", EditorStyles.toolbarButton))
        {
            LogUtility.ClearLogs();
            _filteredLogs.Clear();
        }
        
        // 刷新按钮
        if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
        {
            UpdateLogs();
        }
        
        EditorGUILayout.EndHorizontal();
        
        // 如果筛选条件变化，更新日志列表
        if (csharpChanged != _scriptTypeFilters[0] || luaChanged != _scriptTypeFilters[1] || 
            levelChanged || layerChanged || searchChanged)
        {
            _scriptTypeFilters[0] = csharpChanged;
            _scriptTypeFilters[1] = luaChanged;
            UpdateLogs();
        }
    }
}