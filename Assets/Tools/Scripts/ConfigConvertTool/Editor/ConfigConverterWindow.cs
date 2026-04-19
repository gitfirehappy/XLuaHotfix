using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ConfigConverterWindow : EditorWindow
{
    private ConfigConvertSettings _settings;
    private ConfigConvertChannel _singleChannel;
    private string _singleFilePath = "";
    private Vector2 _scrollPosition;
    
    [MenuItem("Tools/Config Converter")]
    public static void ShowWindow()
    {
        GetWindow<ConfigConverterWindow>("Config Converter");
    }
    
    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        
        EditorGUILayout.Space();
        
        // 总配置文件
        EditorGUILayout.LabelField("总配置", EditorStyles.boldLabel);
        _settings = (ConfigConvertSettings)EditorGUILayout.ObjectField(
            "转换设置", _settings, typeof(ConfigConvertSettings), false);
        
        EditorGUILayout.Space(10);
        
        // 三个方法的板块
        EditorGUILayout.BeginVertical("box");
        
        // 方法1: 转换单个文件
        EditorGUILayout.LabelField("转换单个文件", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        
        // 配置区域
        EditorGUILayout.BeginVertical(GUILayout.Width(300));
        _singleChannel = (ConfigConvertChannel)EditorGUILayout.ObjectField(
            "通道配置", _singleChannel, typeof(ConfigConvertChannel), false);
        
        EditorGUILayout.BeginHorizontal();
        _singleFilePath = EditorGUILayout.TextField("文件路径", _singleFilePath);
        if (GUILayout.Button("浏览", GUILayout.Width(60)))
        {
            string extension = _singleChannel != null ? 
                GetExtensionForFormat(_singleChannel.inputFormat) : "";
                
            string path = EditorUtility.OpenFilePanel("选择文件", Application.dataPath, extension);
            
            if (!string.IsNullOrEmpty(path))
            {
                _singleFilePath = path;
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();  
        
        // 转换按钮
        EditorGUI.BeginDisabledGroup(_singleChannel == null || string.IsNullOrEmpty(_singleFilePath));
        if (GUILayout.Button("转换单个文件", GUILayout.Height(40)))
        {
            ConfigConverter.Instance.ConvertFile(_singleFilePath, _singleChannel);
        }
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(10);
        
        // 方法2: 转换单个通道
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("转换单个通道", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        
        // 配置区域
        EditorGUILayout.BeginVertical(GUILayout.Width(300));
        _singleChannel = (ConfigConvertChannel)EditorGUILayout.ObjectField(
            "通道配置", _singleChannel, typeof(ConfigConvertChannel), false);
        EditorGUILayout.EndVertical();
        
        // 转换按钮
        EditorGUI.BeginDisabledGroup(_singleChannel == null);
        if (GUILayout.Button("转换单个通道", GUILayout.Height(40)))
        {
            ConfigConverter.Instance.Convert(_singleChannel);
        }
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(10);
        
        // 方法3: 转换所有通道
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("转换所有通道", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        
        // 配置区域
        EditorGUILayout.BeginVertical(GUILayout.Width(300));
        if (_settings != null)
        {
            EditorGUILayout.LabelField($"共 {(_settings.channels != null ? _settings.channels.Count : 0)} 个通道");
            
            // 显示所有通道的简要信息
            if (_settings.channels != null && _settings.channels.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("通道列表:", EditorStyles.miniBoldLabel);
                foreach (var channel in _settings.channels)
                {
                    EditorGUILayout.LabelField($"- {channel.name}: {channel.inputFormat} → {channel.outputFormat}", EditorStyles.label);
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("请先选择总配置", MessageType.Info);
        }
        EditorGUILayout.EndVertical();
        
        // 转换按钮
        EditorGUI.BeginDisabledGroup(_settings == null || _settings.channels == null || _settings.channels.Count == 0);
        if (GUILayout.Button("转换所有通道", GUILayout.Height(40)))
        {
            ConfigConverter.Instance.ConvertAll(_settings);
        }
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.EndScrollView();
    }
    
    private string GetExtensionForFormat(ConfigFormat format)
    {
        switch (format)
        {
            case ConfigFormat.Csv:
                return "csv";
            case ConfigFormat.Lua:
                return "lua";
            case ConfigFormat.Json:
                return "json";
            case ConfigFormat.Xml:
                return "xml";
            default:
                return "";
        }
    }
}