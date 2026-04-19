using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;


/// <summary>
/// 类型选择编辑器窗口
/// </summary>
public class TypeSelectionWindow : EditorWindow
{
    private string searchFilter = "";
    private Vector2 scrollPosition;
    private SerializedProperty targetProperty;
    private Type[] filteredTypes;
    private static Type[] allTypesCache; // 全局类型缓存
    private Action<Type> onTypeSelected; // 类型选择完成后的回调

    // 静态构造函数初始化缓存
    static TypeSelectionWindow()
    {
        CacheAssemblyTypes();
    }

    /// <summary>
    /// 缓存程序集类型
    /// </summary>
    private static void CacheAssemblyTypes()
    {
        if (allTypesCache != null) return;

        // 包含所有非动态程序集
        allTypesCache = AppDomain.CurrentDomain.GetAssemblies()
            .Where(asm => !asm.IsDynamic)
            .SelectMany(asm =>
            {
                try
                {
                    return asm.GetTypes();
                }
                catch
                {
                    return Array.Empty<Type>();
                }
            })
            // 包含公共类型、接口和值类型
            .Where(t => t.IsPublic || t.IsInterface || t.IsValueType)
            .OrderBy(t => t.FullName)
            .ToArray();
    }

    public static void Open(SerializedProperty property, Action<Type> onSelected)
    {
        var window = GetWindow<TypeSelectionWindow>("Select Type");
        window.targetProperty = property;
        window.onTypeSelected = onSelected; // 保存回调
        window.minSize = new Vector2(400, 500);
        CacheAssemblyTypes();
    }

    private void OnGUI()
    {
        if (targetProperty == null)
        {
            Close();
            return;
        }

        try
        {
            // 使用正确的样式名称
            GUIStyle searchFieldStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField;
            GUIStyle cancelButtonStyle = GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.miniButton;

            // 搜索栏
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                // 使用安全的样式获取方式
                searchFilter = EditorGUILayout.TextField(searchFilter, searchFieldStyle);

                // 修复3: 添加null检查
                if (GUILayout.Button("", cancelButtonStyle))
                {
                    searchFilter = "";
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();

            // 类型列表
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            {
                // 清空选项
                if (GUILayout.Button("None"))
                {
                    SetType(null);
                    Close();
                }

                // 应用搜索过滤
                if (allTypesCache != null)
                {
                    filteredTypes = string.IsNullOrEmpty(searchFilter)
                        ? allTypesCache.Take(300).ToArray()
                        : allTypesCache
                            .Where(t => t.FullName.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                            .Take(200)
                            .ToArray();

                    // 显示类型
                    foreach (Type type in filteredTypes)
                    {
                        string label = $"{type.Namespace}.{type.Name}";
                        if (GUILayout.Button(label, EditorStyles.label))
                        {
                            SetType(type);
                            Close();
                        }
                    }
                }
                else
                {
                    GUILayout.Label("No types available", EditorStyles.centeredGreyMiniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        catch (Exception ex)
        {
            LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Error,
                $"Type selection error: {ex.Message}");
            Close();
        }
    }

    /// <summary>
    /// 更新序列化属性
    /// </summary>
    /// <param name="type"></param>
    private void SetType(Type type)
    {
        if (targetProperty == null)
        {
            Debug.LogError("SetType: targetProperty is null!");
            return;
        }

        LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Info,
            $"Setting type for property: {targetProperty.propertyPath}");
        LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Info,
            $"Property type: {targetProperty.type}");

        // 处理托管引用
        if (targetProperty.propertyType == SerializedPropertyType.ManagedReference)
        {
            // 获取或创建 TypeReference 实例
            if (targetProperty.managedReferenceValue == null)
            {
                targetProperty.managedReferenceValue = new TypeReference();
            }
            
            // 直接访问 TypeReference 对象
            var typeRef = targetProperty.managedReferenceValue as TypeReference;
            if (typeRef != null)
            {
                typeRef.assemblyName = type?.Assembly.GetName().Name ?? "";
                typeRef.typeName = type?.FullName ?? "";
                
                LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Info,
                    $"Set assemblyName: {typeRef.assemblyName}");
                LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Info,
                    $"Set typeName: {typeRef.typeName}");
                
                // 标记对象已修改
                EditorUtility.SetDirty(targetProperty.serializedObject.targetObject);
            }
            else
            {
                LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Error,
                    "Failed to cast managed reference to TypeReference");
            }
        }
        else
        {
            // 处理普通序列化属性
            SerializedProperty assemblyProp = targetProperty.FindPropertyRelative("assemblyName");
            SerializedProperty typeProp = targetProperty.FindPropertyRelative("typeName");


            if (assemblyProp == null)
            {
                LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Error,
                    "assemblyName property not found! Available properties:");
                ShowChildProperties(targetProperty);
            }
            else
            {
                assemblyProp.stringValue = type?.Assembly.GetName().Name ?? "";
                LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Info,
                    $"Set assemblyName: {assemblyProp.stringValue}");
            }

            if (typeProp == null)
            {
                LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Error,
                    "typeName property not found! Available properties:");
                ShowChildProperties(targetProperty);
            }
            else
            {
                typeProp.stringValue = type?.FullName ?? "";
                LogUtility.Log(LogLayer.Core, "TypeSelectionWindow", LogLevel.Info,
                    $"Set typeName: {typeProp.stringValue}");
            }
        }

        // 确保应用修改
        targetProperty.serializedObject.ApplyModifiedProperties();
        
        // 调用选择完成回调
        onTypeSelected?.Invoke(type);
    }
    
    private void ShowChildProperties(SerializedProperty prop)
    {
        SerializedProperty iterator = prop.Copy();
        SerializedProperty end = prop.GetEndProperty();
        
        iterator.NextVisible(true);
        while (!SerializedProperty.EqualContents(iterator, end))
        {
            Debug.Log($"- {iterator.propertyPath}: {iterator.type}");
            iterator.NextVisible(false);
        }
    }
}