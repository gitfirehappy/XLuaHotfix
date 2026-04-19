using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LuaBehaviourBridge))]
public class LuaBehaviourBridgeEditor : Editor
{
    private List<LuaBridgeType> selectedBridges = new();
    private bool isInitialized = false;
    private bool showBridges = true;

    private void OnEnable()
    {
        if (!isInitialized)
        {
            SyncBridgesFromComponent();
            isInitialized = true;
        }
    }

    public override void OnInspectorGUI()
    {
        var serializedObject = new SerializedObject(target);
        var configKeyProperty = serializedObject.FindProperty("configKey");
        EditorGUILayout.PropertyField(configKeyProperty, new GUIContent("Config Key", "填写 LuaBehaviourConfigSO 的 Addressable Key"));
        serializedObject.ApplyModifiedProperties();
        
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Bridge Components", EditorStyles.boldLabel);
        showBridges = EditorGUILayout.Foldout(showBridges, "已选桥接组件", true);

        if (showBridges)
        {
            EditorGUI.indentLevel++;
            // 自动遍历所有枚举值（无需手动添加）
            foreach (LuaBridgeType type in Enum.GetValues(typeof(LuaBridgeType)))
            {
                bool isSelected = selectedBridges.Contains(type);
                bool newState = EditorGUILayout.ToggleLeft(type.ToString(), isSelected);

                if (newState != isSelected)
                {
                    if (newState)
                    {
                        AddBridge(type);
                        selectedBridges.Add(type);
                    }
                    else
                    {
                        RemoveBridge(type);
                        selectedBridges.Remove(type);
                    }
                    Repaint();
                }
            }
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.Space();
        if (GUILayout.Button("刷新 Bridge 状态"))
        {
            SyncBridgesFromComponent();
        }
    }
    
    private void SyncBridgesFromComponent()
    {
        selectedBridges.Clear();
        var targetScript = (LuaBehaviourBridge)target;
        var existingBridges = targetScript.GetComponents<IBridge>();
        
        foreach (var bridge in existingBridges)
        {
            if (TryGetBridgeType(bridge.GetType(), out var type))
            {
                selectedBridges.Add(type);
            }
        }
    }

    private void AddBridge(LuaBridgeType type)
    {
        var targetScript = (LuaBehaviourBridge)target;
        Type componentType = GetComponentTypeForBridge(type);
        
        if (componentType != null && targetScript.gameObject.GetComponent(componentType) == null)
        {
            Undo.AddComponent(targetScript.gameObject, componentType);
        }
    }

    private void RemoveBridge(LuaBridgeType type)
    {
        var targetScript = (LuaBehaviourBridge)target;
        Type componentType = GetComponentTypeForBridge(type);
        var component = targetScript.gameObject.GetComponent(componentType);
        
        if (component != null)
        {
            Undo.DestroyObjectImmediate(component);
        }
    }

    /// <summary>
    /// 反射获取枚举对应的桥接类型（自动生成映射，无需手动修改）
    /// </summary>
    private Type GetComponentTypeForBridge(LuaBridgeType type)
    {
        var enumField = typeof(LuaBridgeType).GetField(type.ToString());
        var attribute = enumField.GetCustomAttribute<LuaBridgeTypeMappingAttribute>();
        return attribute?.BridgeType;
    }

    /// <summary>
    /// 反射反向查找桥接类型对应的枚举（自动生成映射，无需手动修改）
    /// </summary>
    private bool TryGetBridgeType(Type componentType, out LuaBridgeType bridgeType)
    {
        // 遍历所有枚举值的特性，查找匹配的类型
        foreach (LuaBridgeType type in Enum.GetValues(typeof(LuaBridgeType)))
        {
            var enumField = typeof(LuaBridgeType).GetField(type.ToString());
            var attribute = enumField.GetCustomAttribute<LuaBridgeTypeMappingAttribute>();
            if (attribute?.BridgeType == componentType)
            {
                bridgeType = type;
                return true;
            }
        }
        
        bridgeType = default;
        return false;
    }
}