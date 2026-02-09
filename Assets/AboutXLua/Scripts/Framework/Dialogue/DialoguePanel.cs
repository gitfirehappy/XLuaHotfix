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

    [Header("效果参数")]
    [Tooltip("打字机速度 (秒/字)")] public float typingSpeed = 0.05f;
    [Tooltip("图片淡入时间")] public float imageFadeInDuration = 0.2f;

    private Coroutine typingCoroutine;

    /// <summary>
    /// 设置对话内容（包含打字机效果）
    /// </summary>
    /// <param name="content">文本内容</param>
    /// <param name="speed">打字速度</param>
    [LuaCallCSharp]
    public void SetDialogueContent(string content, float speed = -1f)
    {
        if (contentText == null) return;
        
        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypewriterEffect(content, speed > 0 ? speed : typingSpeed));
    }

    private IEnumerator TypewriterEffect(string content, float speed)
    {
        contentText.text = "";
        foreach (char c in content)
        {
            contentText.text += c;
            if (speed > 0)
                yield return new WaitForSeconds(speed);
        }
        typingCoroutine = null;
    }
    
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
            int LuaDiff = IsBaseCsharp ? 0 : 1;
            button.onClick.AddListener(() => onOptionSelected?.Invoke(index + LuaDiff)); // 注意：Lua下标从1开始
            
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
        string opLower = operation.ToLower().Trim();
    
        // 优先处理预定义快捷操作
        if (opLower == "hide") {
            HideCharacter(characterName);
            return;
        }
        if (opLower == "show") {
            ShowCharacter(characterName);
            return;
        }
        if (opLower.StartsWith("diff")) {
            SetCharacterExpression(characterName, operation);
            return;
        }
    
        // 动态匹配配置中的位置关键字
        foreach (var posConfig in characterPos) {
            if (string.Equals(posConfig.pos, operation, StringComparison.OrdinalIgnoreCase)) {
                // 传入配置中原始关键字（保留大小写），供 SetCharacterPosition 精确查找
                SetCharacterPosition(characterName, posConfig.pos);
                ShowCharacter(characterName);
                return;
            }
        }

        // 未匹配到任何配置位置
        Debug.LogWarning($"[DialoguePanel] 未在 characterPos 配置中找到位置关键字: '{operation}'");
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
        
        // 设置新位置：设为posConfig子物体
        if (runtimeState.CurrentImage != null)
        {
            runtimeState.CurrentImage.transform.SetParent(posConfig.transform, false);
            runtimeState.CurrentImage.transform.localPosition = Vector3.zero;
            runtimeState.CurrentImage.transform.localScale = Vector3.one;
            // 确保没有被隐藏
            // runtimeState.CurrentImage.gameObject.SetActive(true); // 这里不需要强制显示，由ShowCharacter控制
        }
        
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
        if (runtimeState.CurrentImage != null)
        {
            bool wasActive = runtimeState.CurrentImage.gameObject.activeSelf;
            // 简单检查透明度，避免重复淡入
            bool isVisible = wasActive && runtimeState.CurrentImage.color.a > 0.95f;
            
            runtimeState.CurrentImage.gameObject.SetActive(true);
            
            // 仅当未显示或不可见时执行淡入
            if (!isVisible)
            {
                StartCoroutine(FadeInImage(runtimeState.CurrentImage));
            }
        }
    }

    private IEnumerator FadeInImage(Image img)
    {
        float elapsed = 0;
        Color c = img.color;
        c.a = 0;
        img.color = c;
        
        while (elapsed < imageFadeInDuration)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Clamp01(elapsed / imageFadeInDuration);
            img.color = c;
            yield return null;
        }
        c.a = 1;
        img.color = c;
    }

    /// <summary>
    /// 隐藏角色
    /// </summary>
    /// <param name="characterName">角色名</param>
    private void HideCharacter(string characterName)
    {
        if (activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            // 隐藏角色
            if (runtimeState.CurrentImage != null)
                runtimeState.CurrentImage.gameObject.SetActive(false);

            // 释放位置占用
            if (!string.IsNullOrEmpty(runtimeState.CurrentPos))
            {
                positionOccupancy.Remove(runtimeState.CurrentPos);
                runtimeState.CurrentPos = null;
            }
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

        if (!operation.StartsWith("diff")) return;

        // 提取数字部分
        string indexStr = operation.Substring(4);
        if (!int.TryParse(indexStr, out int diffIndex))
        {
            Debug.LogWarning($"表情差分指令数字解析失败: {operation}");
            return;
        }

        // 验证索引有效性
        if (diffIndex < 0 || diffIndex >= characterConfig.Images.Count)
        {
            Debug.LogWarning($"角色{characterName}表情索引越界: {diffIndex}");
            return;
        }

        // 获取或创建运行时状态
        if (!activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            runtimeState = new CharacterRuntimeState
            {
                CharacterName = characterName,
                CurrentImage = CreateCharacterImage(characterName, characterConfig.Images[diffIndex]),
                CurrentDiffIndex = diffIndex
            };
            activeCharacters[characterName] = runtimeState;
        }
        else
        {
            // 更新现有角色的表情
            if (runtimeState.CurrentImage != null)
            {
                 runtimeState.CurrentImage.sprite = characterConfig.Images[diffIndex];
            }
            runtimeState.CurrentDiffIndex = diffIndex;
        }
    }

    /// <summary>
    /// 创建角色Image组件
    /// </summary>
    private Image CreateCharacterImage(string characterName, Sprite defaultSprite)
    {
        if (characterImageParent == null)
        {
            Debug.LogError("[DialoguePanel] 未设置CharacterImageParent");
            return null;
        }
        
        var go = new GameObject($"Character_{characterName}");
        // 初始父节点设为characterImageParent，后续SetPosition会修改
        go.transform.SetParent(characterImageParent);
        go.transform.localScale = Vector3.one;
        go.transform.localPosition = Vector3.zero; // 初始位置相对于父对象归零
        
        // 添加Image组件
        var image = go.AddComponent<Image>();
        image.sprite = defaultSprite;
        image.raycastTarget = false; // 不响应点击
        
        // 初始透明度设为0 (为了支持FadeIn)
        Color c = image.color;
        c.a = 0;
        image.color = c;
        
        go.SetActive(false); // 默认隐藏，由ShowCharacter显示

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
        // 回退为 Sprite
        public List<Sprite> Images; 
    }
    
    /// <summary>
    /// 当前活跃的角色状态
    /// </summary> 
    private class CharacterRuntimeState
    {
        public string CharacterName;
        // 回退为仅引用 Image
        public Image CurrentImage; 

        public string CurrentPos = "";
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

