using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XLua;

/// <summary>
/// 对话系统面板，Csharp和Lua共用
/// </summary>
public class DialoguePanel : UIFormBase,IPointerClickHandler
{
    [Header("基于C#实现还是Lua实现")]
    public bool IsBaseCsharp = false;
    
    [Header("UI组件")]
    [Tooltip("对话内容")] public TextMeshProUGUI contentText;
    [Tooltip("角色名称")] public TextMeshProUGUI characterNameText;
    [Tooltip("选项父物体")] public Transform optionsParent;
    [Tooltip("选项预制体")] public GameObject optionPrefab;
    [Tooltip("角色图片父物体")] public Transform characterImageParent; 
    
    [Header("静态配置")]
    [Tooltip("角色差分图")][SerializeField] private List<CharacterImageSources> characterImages;
    [Tooltip("角色位置设置")][SerializeField] private List<PosCanChoose> characterPos;
    
    private List<GameObject> currentOptions = new();
    
    ///<summary>名字 -> 当前状态 </summary>
    private Dictionary<string, CharacterRuntimeState> activeCharacters = new();
    
    ///<summary>位置 -> 角色名 </summary>>
    private Dictionary<string, string> positionOccupancy = new();
    
    /// <summary>
    /// 创建并显示选项
    /// </summary>
    [LuaCallCSharp]
    public void CreateOptions(List<string> optionTexts, Action<int> onOptionSelected)
    {
        ClearOptions();
        
        if (optionTexts == null || optionPrefab == null || optionsParent == null) 
            return;
            
        for (int i = 0; i < optionTexts.Count; i++)
        {
            var optionObj = Instantiate(optionPrefab, optionsParent);
            var button = optionObj.GetComponent<Button>();
            var text = optionObj.GetComponentInChildren<TextMeshProUGUI>();
            
            if (text != null)
                text.text = optionTexts[i];
                
            int index = i; // 闭包捕获
            button.onClick.AddListener(() => onOptionSelected?.Invoke(index + 1));// 注意：Lua下标从1开始
            
            currentOptions.Add(optionObj);
        }
        
        optionsParent.gameObject.SetActive(true);
    }

    /// <summary>
    /// 清空并隐藏选项
    /// </summary>
    [LuaCallCSharp]
    public void ClearOptions()
    {
        foreach (var option in currentOptions)
        {
            if (option != null)
                Destroy(option);
        }
        currentOptions.Clear();
        
        if (optionsParent != null)
            optionsParent.gameObject.SetActive(false);
    }

    /// <summary>
    /// 更新角色状态
    /// </summary>
    /// <param name="characterNames">需要调整的角色名称</param>
    /// <param name="posAndOps">位置或快捷操作</param>
    [LuaCallCSharp]
    public void UpdateCharacter(List<string> characterNames, List<string> posAndOps)
    {
        if (characterNames == null || characterNames.Count == 0) return;
        
        // 根据传入的参数对角色做对应的调整
        for (int i = 0; i < characterNames.Count && i < characterImages.Count; i++)
        {
            if (i < posAndOps.Count)
            {
                var operations = StringUtil.SplitAmpersand(posAndOps[i]); // 处理&分隔
                foreach (var operation in operations)
                {
                    ExecuteOperation(characterNames[i], operation);
                }
            }
        }
    }
    
    /// <summary>
    /// 执行单个操作
    /// </summary>
    private void ExecuteOperation(string characterName, string operation)
    {
        switch (operation.ToLower())
        {
            case "left":
                SetCharacterPosition(characterName, "left");
                ShowCharacter(characterName);
                break;
            case "right":
                SetCharacterPosition(characterName, "right");
                ShowCharacter(characterName);
                break;
            case "center":
                SetCharacterPosition(characterName, "center");
                ShowCharacter(characterName);
                break;
            case "hide":
                HideCharacter(characterName);
                break;
            case "show":
                ShowCharacter(characterName);
                break;
            default:
                // 检查是否为图片差分操作（diff+数字）
                if (operation.StartsWith("diff"))
                {
                    SetCharacterExpression(characterName, operation);
                }
                break;
        }
    }

    #region 具体快捷操作
    
    private void SetCharacterPosition(string characterName, string pos)
    {
        // 查找角色配置
        var characterConfig = characterImages.Find(c => c.Name == characterName);
        if (characterConfig == null)
        {
            Debug.LogWarning($"[DialoguePanel] 未找到角色配置: {characterName}");
            return;
        }
        
        // 查找位置配置
        var posConfig = characterPos.Find(p => p.pos.ToLower() == pos.ToLower());
        if (posConfig == null || posConfig.transform == null)
        {
            Debug.LogWarning($"[DialoguePanel] 未找到位置配置: {pos}");
            return;
        }
        
        // 获取或创建运行时状态
        if (!activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            runtimeState = new CharacterRuntimeState
            {
                CharacterName = characterName,
                CurrentImage = CreateCharacterImage(characterName, characterConfig.Images[0])
            };
            activeCharacters[characterName] = runtimeState;
        }
        
        // 处理位置占用
        if (!string.IsNullOrEmpty(runtimeState.CurrentPos))
        {
            positionOccupancy.Remove(runtimeState.CurrentPos);
        }
        
        // 设置新位置
        runtimeState.CurrentImage.rectTransform.localPosition = posConfig.transform.localPosition;
        runtimeState.CurrentPos = pos;
        positionOccupancy[pos] = characterName;
    }

    private void ShowCharacter(string characterName)
    {
        // 查找角色配置
        var characterConfig = characterImages.Find(c => c.Name == characterName);
        if (characterConfig == null)
        {
            Debug.LogWarning($"[DialoguePanel] 未找到角色配置: {characterName}");
            return;
        }
        
        // 获取或创建运行时状态
        if (!activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            runtimeState = new CharacterRuntimeState
            {
                CharacterName = characterName,
                CurrentImage = CreateCharacterImage(characterName, characterConfig.Images[0])
            };
            activeCharacters[characterName] = runtimeState;
        }
        
        // 显示角色
        runtimeState.CurrentImage.gameObject.SetActive(true);
    }
    
    private void HideCharacter(string characterName)
    {
        if (activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            // 隐藏角色
            runtimeState.CurrentImage.gameObject.SetActive(false);
            
            // 释放位置占用
            if (!string.IsNullOrEmpty(runtimeState.CurrentPos))
            {
                positionOccupancy.Remove(runtimeState.CurrentPos);
                runtimeState.CurrentPos = null;
            }
            
            // 可选：完全移除运行时状态
            // activeCharacters.Remove(characterName);
        }
    }
    
    /// <summary>
    /// 设置角色图片差分
    /// </summary>
    /// <param name="characterName">角色名</param>
    /// <param name="operation">diff + 数字</param>
    private void SetCharacterExpression(string characterName, string operation)
    {
        // 查找角色配置
        var characterConfig = characterImages.Find(c => c.Name == characterName);
        if (characterConfig == null)
        {
            Debug.LogWarning($"未找到角色配置: {characterName}，无法设置表情");
            return;
        }

        // 验证操作格式并提取差分索引
        if (!operation.StartsWith("diff"))
        {
            Debug.LogWarning($"无效的表情差分指令: {operation}，格式应为diff+数字（如diff1）");
            return;
        }

        // 提取数字部分（支持多位数字）
        string indexStr = operation.Substring(4);
        if (!int.TryParse(indexStr, out int diffIndex))
        {
            Debug.LogWarning($"表情差分指令数字解析失败: {operation}");
            return;
        }

        // 验证索引有效性
        if (diffIndex < 0 || diffIndex >= characterConfig.Images.Count)
        {
            Debug.LogWarning($"角色{characterName}表情索引越界: {diffIndex}（有效范围1-{characterConfig.Images.Count}）");
            return;
        }

        // 获取或创建运行时状态
        if (!activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            // 如果角色未激活，创建默认渲染器
            runtimeState = new CharacterRuntimeState
            {
                CharacterName = characterName,
                CurrentImage = CreateCharacterImage(characterName, characterConfig.Images[diffIndex])
            };
            activeCharacters[characterName] = runtimeState;
        }
        else
        {
            // 更新现有角色的表情
            runtimeState.CurrentImage.sprite = characterConfig.Images[diffIndex];
        }

        // 更新当前差分索引
        runtimeState.CurrentDiffIndex = diffIndex;
    }
    
    /// <summary>
    /// 创建角色Image组件
    /// </summary>
    private Image CreateCharacterImage(string characterName, Sprite defaultSprite)
    {
        // 校验父对象是否设置
        if (characterImageParent == null)
        {
            Debug.LogError("[DialoguePanel] 未设置CharacterImageParent，请在Inspector中指定角色图片父对象");
            return null;
        }
        
        var go = new GameObject($"Character_{characterName}");
        // 设置父对象为指定的characterImageParent
        go.transform.SetParent(characterImageParent);
        go.transform.localScale = Vector3.one;
        go.transform.localPosition = Vector3.zero; // 初始位置相对于父对象归零
        
        // 添加Image组件
        var image = go.AddComponent<Image>();
        image.sprite = defaultSprite;
        image.raycastTarget = false; // 不响应点击
        
        // 层级由父对象所在Canvas统一管理，无需单独设置Canvas组件
        return image;
    }
    
    #endregion 

    #region 复合结构
    
    [Serializable]
    public class PosCanChoose
    {
        public string pos;
        public Transform transform;
    }

    [Serializable]
    public class CharacterImageSources
    {
        public string Name;
        public List<Sprite> Images;
    }
    
    /// <summary>
    /// 当前活跃的角色状态
    /// </summary> 
    private class CharacterRuntimeState
    {
        public string CharacterName;
        public Image CurrentImage;
        public string CurrentPos = "center";
        public int CurrentDiffIndex = 0;
    }
    
    #endregion
    
    /// <summary>
    /// 清理所有角色状态
    /// </summary>
    public void ClearAllCharacters()
    {
        foreach (var runtimeState in activeCharacters.Values)
        {
            if (runtimeState.CurrentImage != null)
            {
                Destroy(runtimeState.CurrentImage.gameObject);
            }
        }
        
        activeCharacters.Clear();
        positionOccupancy.Clear();
    }
    
    /// <summary>
    /// Csharp版本点击下一句
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if(!IsBaseCsharp) return;
        
        DialogueController.Next();
    }
}

