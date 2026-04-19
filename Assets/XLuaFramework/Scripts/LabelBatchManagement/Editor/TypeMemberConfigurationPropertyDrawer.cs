using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(TypeMemberConfiguration))]
public class TypeMemberConfigurationPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        
        var typeRefProp = property.FindPropertyRelative("typeRef");
        var memberRefProp = property.FindPropertyRelative("memberRef");
        var isEntireTypeProp = property.FindPropertyRelative("isEntireType");
        
        // 主标签区域
        Rect mainRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        
        // 使用序列化属性获取显示文本
        string displayText = GetDisplayText(property);
        
        if (EditorGUI.DropdownButton(mainRect, new GUIContent(displayText), FocusType.Keyboard))
        {
            ShowConfigMenu(property);
        }
        
        EditorGUI.EndProperty();
    }
    
    private string GetDisplayText(SerializedProperty property)
    {
        var typeRefProp = property.FindPropertyRelative("typeRef");
        var isEntireTypeProp = property.FindPropertyRelative("isEntireType");
        var memberRefProp = property.FindPropertyRelative("memberRef");
        
        if (typeRefProp == null) 
            return "None";
        
        string typeName = typeRefProp.FindPropertyRelative("typeName")?.stringValue;
        if (string.IsNullOrEmpty(typeName))
            return "None";
        
        string shortTypeName = typeName.Split('.').Last();
        
        if (isEntireTypeProp.boolValue)
        {
            return $"Type: {shortTypeName}";
        }
        else
        {
            string memberName = memberRefProp?.FindPropertyRelative("memberName")?.stringValue;
            if (string.IsNullOrEmpty(memberName))
                return $"Member: {shortTypeName}.[Select]";
            
            return $"Member: {shortTypeName}.{memberName}";
        }
    }
    
    private void ShowConfigMenu(SerializedProperty property)
    {
        GenericMenu menu = new GenericMenu();
        
        // 添加类型选项
        menu.AddItem(new GUIContent("Select Entire Type"), false, () => {
            property.serializedObject.Update();
            var typeRefProp = property.FindPropertyRelative("typeRef");
            
            TypeSelectionWindow.Open(typeRefProp, (selectedType) => {
                if (selectedType != null)
                {
                    property.FindPropertyRelative("isEntireType").boolValue = true;
                    property.FindPropertyRelative("memberRef").managedReferenceValue = null;
                    property.serializedObject.ApplyModifiedProperties();
                }
            });
        });
        
        // 添加成员选项
        menu.AddItem(new GUIContent("Select Specific Member"), false, () => {
            property.serializedObject.Update();
            var typeRefProp = property.FindPropertyRelative("typeRef");
            
            TypeSelectionWindow.Open(typeRefProp, (selectedType) => {
                if (selectedType != null)
                {
                    property.FindPropertyRelative("isEntireType").boolValue = false;
                    var memberRefProp = property.FindPropertyRelative("memberRef");
                    
                    // 确保成员引用的所有者类型正确
                    var memberRef = memberRefProp.managedReferenceValue as MemberReference ?? new MemberReference();
                    memberRef.ownerType = new TypeReference {
                        assemblyName = selectedType.Assembly.GetName().Name,
                        typeName = selectedType.FullName
                    };
                    memberRefProp.managedReferenceValue = memberRef;
                    property.serializedObject.ApplyModifiedProperties();

                    MemberSelectionWindow.Open(memberRefProp);
                }
            });
        });
        
        // 清除选项
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Clear Configuration"), false, () => {
            property.serializedObject.Update();
            property.FindPropertyRelative("typeRef").managedReferenceValue = null;
            property.FindPropertyRelative("memberRef").managedReferenceValue = null;
            property.serializedObject.ApplyModifiedProperties();
        });
        
        menu.ShowAsContext();
    }
}