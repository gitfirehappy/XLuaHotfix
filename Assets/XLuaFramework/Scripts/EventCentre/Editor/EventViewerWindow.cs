using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class EventViewerWindow : EditorWindow
{
    private Vector2 _scrollPosition;
    private Dictionary<string, List<EventInfo>> _eventsByPort = new Dictionary<string, List<EventInfo>>();
    private string _searchText = "";
    private bool[] _portFilters;
    private List<EventCentre.EventPort> _portValues;
    
    [MenuItem("XLua/调试/Event Viewer")]
    public static void ShowWindow()
    {
        GetWindow<EventViewerWindow>("Event Viewer");
    }

    private void OnEnable()
    {
        // 获取事件端口枚举值
        _portValues = new List<EventCentre.EventPort>((EventCentre.EventPort[])Enum.GetValues(typeof(EventCentre.EventPort)));
        _portFilters = new bool[_portValues.Count];
        for (int i = 0; i < _portFilters.Length; i++) _portFilters[i] = true;
        
        // 订阅事件中心的变化
        EventCentre.Instance.OnEventChanged += RefreshEvents;
        RefreshEvents();
    }

    private void OnDisable()
    {
        // 取消订阅
        if (EventCentre.Instance != null)
        {
            EventCentre.Instance.OnEventChanged -= RefreshEvents;
        }
    }

    private void OnGUI()
    {
        DrawToolbar();
        
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        
        foreach (var port in _portValues)
        {
            if (!_portFilters[_portValues.IndexOf(port)]) continue;
            
            string portKey = port.ToString();
            if (_eventsByPort.ContainsKey(portKey))
            {
                // 只有当端口中有匹配搜索条件的事件时才显示该端口
                var filteredEvents = _eventsByPort[portKey].Where(e => 
                    string.IsNullOrEmpty(_searchText) || e.Name.Contains(_searchText)).ToList();
                
                if (filteredEvents.Count > 0)
                {
                    EditorGUILayout.LabelField($"{portKey} ({filteredEvents.Count} events)", EditorStyles.boldLabel);
                    
                    foreach (var eventInfo in filteredEvents)
                    {
                        EditorGUILayout.BeginVertical("Box");
                        EditorGUILayout.LabelField($"Name: {eventInfo.Name}");
                        EditorGUILayout.LabelField($"Listeners: {eventInfo.ListenerCount}");
                        EditorGUILayout.EndVertical();
                    }
                }
            }
        }
        
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        // 端口筛选
        EditorGUILayout.LabelField("Ports:", GUILayout.Width(40));
        for (int i = 0; i < _portValues.Count; i++)
        {
            _portFilters[i] = GUILayout.Toggle(_portFilters[i], _portValues[i].ToString(), EditorStyles.toolbarButton);
        }
        
        // 搜索
        EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
        _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarTextField);
        
        GUILayout.FlexibleSpace();
        
        // 刷新按钮
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
        {
            RefreshEvents();
        }
        
        EditorGUILayout.EndHorizontal();
    }

    private void RefreshEvents()
    {
        _eventsByPort.Clear();
        
        var eventCentre = EventCentre.Instance;
        if (eventCentre == null) return;
        
        // 使用反射获取事件字典
        var fieldInfo = typeof(EventCentre).GetField("eventDictionaries", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fieldInfo == null) return;
        
        var eventDictionaries = fieldInfo.GetValue(eventCentre) as Dictionary<EventCentre.EventPort, Dictionary<string, Delegate>>;
        if (eventDictionaries == null) return;
        
        foreach (var port in _portValues)
        {
            string portKey = port.ToString();
            _eventsByPort[portKey] = new List<EventInfo>();
            
            if (eventDictionaries.ContainsKey(port))
            {
                foreach (var eventPair in eventDictionaries[port])
                {
                    int listenerCount = eventPair.Value?.GetInvocationList().Length ?? 0;
                    _eventsByPort[portKey].Add(new EventInfo
                    {
                        Name = eventPair.Key,
                        ListenerCount = listenerCount
                    });
                }
            }
        }
        
        Repaint();
    }
    
    private class EventInfo
    {
        public string Name { get; set; }
        public int ListenerCount { get; set; }
    }
}
