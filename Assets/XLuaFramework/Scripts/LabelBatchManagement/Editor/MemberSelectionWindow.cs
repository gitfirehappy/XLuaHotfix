using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;

public class MemberSelectionWindow : EditorWindow
{
    private SerializedProperty targetProperty;
    private Vector2 scrollPosition;
    private string searchFilter = "";
    private MemberInfo[] filteredMembers;
    
    private Type ownerType;
    private static Dictionary<Type, MemberInfo[]> membersCache = new Dictionary<Type, MemberInfo[]>();
    
    public static void Open(SerializedProperty property)
    {
        var window = GetWindow<MemberSelectionWindow>("Select Member");
        window.targetProperty = property;
        window.minSize = new Vector2(400, 500);
        
        // 获取所属类型
        SerializedProperty ownerTypeProp = property.FindPropertyRelative("ownerType");
        string assemblyName = ownerTypeProp.FindPropertyRelative("assemblyName").stringValue;
        string typeName = ownerTypeProp.FindPropertyRelative("typeName").stringValue;
        
        if (!string.IsNullOrEmpty(assemblyName) && !string.IsNullOrEmpty(typeName))
        {
            try
            {
                window.ownerType = Assembly.Load(assemblyName)?.GetType(typeName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load type: {assemblyName}/{typeName}. Error: {ex.Message}");
            }
        }
        
        // 即使类型加载失败，也保持窗口打开显示错误信息
    }
    
    private static MemberInfo[] GetAllMembers(Type type)
    {
        if (type == null) return Array.Empty<MemberInfo>();
        
        List<MemberInfo> members = new List<MemberInfo>();
        
        // 添加方法
        members.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.IsSpecialName)); // 排除属性访问器等特殊方法
        
        // 添加属性
        members.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        
        // 添加字段
        members.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        
        return members.OrderBy(m => m.Name).ToArray();
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
                searchFilter = EditorGUILayout.TextField(searchFilter, searchFieldStyle);
                
                if (GUILayout.Button("", cancelButtonStyle))
                {
                    searchFilter = "";
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // 成员列表
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            {
                // 清空选项
                if (GUILayout.Button("None"))
                {
                    SetMember(null);
                    Close();
                }
                
                // 显示错误信息（如果类型加载失败）
                if (ownerType == null)
                {
                    EditorGUILayout.HelpBox("Failed to load owner type. Please ensure the type is valid.", MessageType.Error);
                    EditorGUILayout.EndScrollView();
                    return;
                }
                
                // 确保成员已缓存
                if (!membersCache.ContainsKey(ownerType))
                {
                    membersCache[ownerType] = GetAllMembers(ownerType);
                }
                
                var allMembers = membersCache[ownerType];
                
                // 应用搜索过滤
                filteredMembers = string.IsNullOrEmpty(searchFilter) 
                    ? allMembers.Take(300).ToArray() 
                    : allMembers
                        .Where(m => m.Name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Take(200)
                        .ToArray();
                
                // 显示成员数量信息
                EditorGUILayout.LabelField($"Found {filteredMembers.Length} members", EditorStyles.miniLabel);
                
                // 显示成员
                foreach (MemberInfo member in filteredMembers)
                {
                    string label = GetMemberLabel(member);
                    if (GUILayout.Button(label, EditorStyles.label))
                    {
                        SetMember(member);
                        Close();
                    }
                }
                
                // 如果没有找到成员
                if (filteredMembers.Length == 0)
                {
                    EditorGUILayout.HelpBox("No members found. Try a different search filter.", MessageType.Info);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        catch (Exception ex)
        {
            LogUtility.Log(LogLayer.Core,"MemberSelectionWindow",LogLevel.Error,
                $"Member selection error: {ex.Message}");
            Close();
        }
    }
    
    private string GetMemberLabel(MemberInfo member)
    {
        if (member is MethodInfo method)
        {
            return $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";
        }
        if (member is PropertyInfo property)
        {
            return $"{property.Name} : {property.PropertyType.Name}";
        }
        if (member is FieldInfo field)
        {
            return $"{field.Name} : {field.FieldType.Name}";
        }
        return member.Name;
    }
    
    private void SetMember(MemberInfo member)
    {
        if (targetProperty == null) return;
        
        SerializedProperty memberNameProp = targetProperty.FindPropertyRelative("memberName");
        SerializedProperty memberTypeProp = targetProperty.FindPropertyRelative("memberType");
        SerializedProperty serializedParamsProp = targetProperty.FindPropertyRelative("_serializedParameters");
        
        if (memberNameProp == null || memberTypeProp == null || serializedParamsProp == null)
        {
            LogUtility.Log(LogLayer.Core, "MemberSelectionWindow", LogLevel.Error, 
                "Failed to find required serialized properties");
            return;
        }
        
        if (member != null)
        {
            memberNameProp.stringValue = member.Name;
            
            if (member is MethodInfo)
            {
                memberTypeProp.enumValueIndex = (int)MemberReference.MemberType.Method;
                
                // 清除现有参数
                serializedParamsProp.ClearArray();
                
                // 保存方法参数类型到序列化列表
                var methodInfo = member as MethodInfo;
                foreach (var param in methodInfo.GetParameters())
                {
                    string assemblyName = param.ParameterType.Assembly.GetName().Name;
                    string typeName = param.ParameterType.FullName;
                    string paramStr = $"{assemblyName}|{typeName}";
                
                    serializedParamsProp.arraySize++;
                    var element = serializedParamsProp.GetArrayElementAtIndex(serializedParamsProp.arraySize - 1);
                    element.stringValue = paramStr; // 直接设置字符串值
                }
            }
            else if (member is PropertyInfo)
            {
                memberTypeProp.enumValueIndex = (int)MemberReference.MemberType.Property;
                
                // 清除参数（属性没有参数）
                serializedParamsProp.ClearArray();
            }
            else if (member is FieldInfo)
            {
                memberTypeProp.enumValueIndex = (int)MemberReference.MemberType.Field;
                
                // 清除参数（字段没有参数）
                serializedParamsProp.ClearArray();
            }
        }
        else
        {
            memberNameProp.stringValue = "";
            memberTypeProp.enumValueIndex = 0; // 默认为Method
            serializedParamsProp.ClearArray();
        }
        
        targetProperty.serializedObject.ApplyModifiedProperties();
    }
}