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
    [Tooltip("角色差分图配置")][SerializeField] private List<CharacterConfig> characterConfigs;
    [Tooltip("角色位置设置")][SerializeField] private List<PosCanChoose> characterPos;
    
    private List<GameObject> currentOptions = new();
    
    ///<summary>名字 -> 当前状态 </summary>
    private Dictionary<string, CharacterRuntimeState> activeCharacters = new();
    
    ///<summary>位置 -> 角色名 </summary>>
    private Dictionary<string, string> positionOccupancy = new();

    [Header("效果参数")]
    [Tooltip("打字机速度 (秒/字)")] public float typingSpeed = 0.05f;
    [Tooltip("图片淡入时间")] public float imageFadeInDuration = 0.2f;
    [Tooltip("文本字符淡入时间")] public float textFadeDuration = 0.03f;
    [Tooltip("角色图片切换淡入时间")] public float imageDiffFadeDuration = 0.2f;
    [Tooltip("角色图片切换淡入透明度")] public float imageDiffFadeAlpha = 0.8f;

    private Coroutine typingCoroutine;
    private Coroutine expressionCoroutine;

    /// <summary>
    /// 设置对话内容（包含打字机效果）
    /// </summary>
    /// <param name="content">文本内容</param>
    /// <param name="speed">打字速度</param>
    [LuaCallCSharp]
    public void SetDialogueContent(string content, float speed = -1f)
    {
        if (contentText == null) return;

        // 处理换行符：将字面量\n转换为实际换行
        content = content.Replace("\\n", "\n");
        
        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypewriterEffect(content, speed > 0 ? speed : typingSpeed));
    }

    private IEnumerator TypewriterEffect(string content, float speed)
    {
        contentText.text = content;
        contentText.ForceMeshUpdate();

        TMP_TextInfo textInfo = contentText.textInfo;
        int totalChars = textInfo.characterCount;
        
        // 预计算每个字符的开始时间
        float[] startTimes = new float[totalChars];
        for (int i = 0; i < totalChars; i++)
        {
            startTimes[i] = i * speed;
        }

        // 初始化所有字符为透明
        Color32[] newVertexColors;
        Color32 c0 = contentText.color;
        c0.a = 0;
        
        for (int i = 0; i < totalChars; i++)
        {
            if (!textInfo.characterInfo[i].isVisible) continue;
            int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
            newVertexColors = textInfo.meshInfo[materialIndex].colors32;
            int vertexIndex = textInfo.characterInfo[i].vertexIndex;
            
            newVertexColors[vertexIndex + 0] = c0;
            newVertexColors[vertexIndex + 1] = c0;
            newVertexColors[vertexIndex + 2] = c0;
            newVertexColors[vertexIndex + 3] = c0;
        }
        contentText.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);

        float time = 0;
        bool allDone = false;
        
        while (!allDone)
        {
            allDone = true;
            time += Time.deltaTime;
            
            for (int i = 0; i < totalChars; i++)
            {
                if (!textInfo.characterInfo[i].isVisible) continue;

                float charTime = time - startTimes[i];
                if (charTime < 0) 
                {
                    allDone = false; // 还没开始
                    continue; 
                }

                if (charTime < textFadeDuration)
                {
                    allDone = false; // 还没结束
                    byte alpha = (byte)Mathf.Lerp(0, 255, charTime / textFadeDuration);
                    
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
                    newVertexColors = textInfo.meshInfo[materialIndex].colors32;
                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;
                    
                    Color32 c = contentText.color;
                    c.a = alpha;

                    newVertexColors[vertexIndex + 0] = c;
                    newVertexColors[vertexIndex + 1] = c;
                    newVertexColors[vertexIndex + 2] = c;
                    newVertexColors[vertexIndex + 3] = c;
                }
                else
                {
                    // 已完成，确保完全不透明
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
                    newVertexColors = textInfo.meshInfo[materialIndex].colors32;
                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;
                    
                    if (newVertexColors[vertexIndex + 0].a != 255)
                    {
                        Color32 c = contentText.color;
                        c.a = 255;
                        newVertexColors[vertexIndex + 0] = c;
                        newVertexColors[vertexIndex + 1] = c;
                        newVertexColors[vertexIndex + 2] = c;
                        newVertexColors[vertexIndex + 3] = c;
                    }
                }
            }
            
            contentText.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            yield return null;
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
        
        List<CharacterInstruction> instructions = new List<CharacterInstruction>();

        // 1. 解析所有指令
        for (int i = 0; i < characterNames.Count; i++)
        {
            string charName = characterNames[i];
            // 若角色名为空，不进行操作（但可能占位），根据需求此处直接跳过
            if (string.IsNullOrEmpty(charName)) continue;

            string opsStr = (i < posAndOps.Count) ? posAndOps[i] : "";
            instructions.Add(ParseInstruction(charName, opsStr));
        }

        // 2. 优先执行 Hide
        foreach (var instr in instructions)
        {
            if (instr.HasHide)
            {
                HideCharacter(instr.CharacterName);
            }
        }

        // 3. 执行 Position / Diff / Show
        foreach (var instr in instructions)
        {
            // 检查角色配置是否存在，若不存在则忽略
            var config = characterConfigs?.Find(c => c.CharacterName == instr.CharacterName);
            if (config == null) continue;

            // 处理位置变动 & 替换 (顶掉角色时淡入淡出)
            if (!string.IsNullOrEmpty(instr.TargetPos))
            {
                // 检查目标位置占用
                if (positionOccupancy.TryGetValue(instr.TargetPos, out string occupierName))
                {
                    // 若被其他人占用，且那个人不在当前Hide指令中（避免重复Hide），则强制Hide
                    if (occupierName != instr.CharacterName)
                    {
                        HideCharacter(occupierName);
                    }
                }

                // 应用位置和表情
                // 允许单独处理diff (此处是组合情况)
                // Diff继承 (若无TargetDiff，SetCharacterPosition不改图片，即继承)
                if (!string.IsNullOrEmpty(instr.TargetDiff))
                {
                    SetCharacterPosAndExpression(instr.CharacterName, instr.TargetPos, instr.TargetDiff);
                }
                else
                {
                    SetCharacterPosition(instr.CharacterName, instr.TargetPos);
                }

                // 确显示
                ShowCharacter(instr.CharacterName);
            }
            else if (!string.IsNullOrEmpty(instr.TargetDiff))
            {
                // 单独处理diff（不变动位置）
                SetCharacterExpression(instr.CharacterName, instr.TargetDiff);
            }

            // 处理显式Show (若未包含在Position逻辑中)
            if (instr.HasShow)
            {
                ShowCharacter(instr.CharacterName);
            }
        }
    }

    private class CharacterInstruction
    {
        public string CharacterName;
        public string TargetPos;
        public string TargetDiff;
        public bool HasHide;
        public bool HasShow;
    }

    private CharacterInstruction ParseInstruction(string charName, string opsStr)
    {
        var instr = new CharacterInstruction { CharacterName = charName };
        var operations = StringUtil.SplitAmpersand(opsStr);

        foreach (var op in operations)
        {
            string opLower = op.ToLower().Trim();
            if (string.IsNullOrEmpty(opLower)) continue;

            if (opLower == "hide")
            {
                instr.HasHide = true;
            }
            else if (opLower == "show")
            {
                instr.HasShow = true;
            }
            else if (opLower.StartsWith("diff"))
            {
                instr.TargetDiff = op;
            }
            else
            {
                // 尝试匹配位置
                bool isPos = false;
                foreach (var posConfig in characterPos)
                {
                    if (string.Equals(posConfig.pos, op, StringComparison.OrdinalIgnoreCase))
                    {
                        instr.TargetPos = posConfig.pos; // 保持原始大小写
                        isPos = true;
                        break;
                    }
                }
                if (!isPos)
                {
                    Debug.LogWarning($"[DialoguePanel] 未识别的操作符: {op}");
                }
            }
        }
        return instr;
    }

    #region 具体快捷操作

    /// <summary>
    /// 设置角色位置
    /// </summary>
    /// <param name="characterName">角色名</param>
    /// <param name="pos">位置名</param>
    private void SetCharacterPosition(string characterName, string pos)
    {
        // 查找角色配置
        var characterConfig = characterConfigs?.Find(c => c.CharacterName == characterName);
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
                CurrentImage = CreateCharacterImage(characterName, characterConfig.GetSprite(0))
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

    /// <summary>
    /// 显示角色
    /// </summary>
    /// <param name="characterName">角色名</param>
    private void ShowCharacter(string characterName)
    {
        // 查找角色配置
        var characterConfig = characterConfigs?.Find(c => c.CharacterName == characterName);
        if (characterConfig == null) return; 
        
        // 获取或创建运行时状态
        if (!activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            runtimeState = new CharacterRuntimeState
            {
                CharacterName = characterName,
                // 默认使用配置的第一张图（索引0）作为初始图像
                CurrentImage = CreateCharacterImage(characterName, characterConfig.GetSprite(0)) 
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
            // 隐藏角色 - 改为淡出
            if (runtimeState.CurrentImage != null && runtimeState.CurrentImage.gameObject.activeSelf)
            {
                 StartCoroutine(FadeOutImage(runtimeState.CurrentImage));
            }
            else if (runtimeState.CurrentImage != null)
            {
                runtimeState.CurrentImage.gameObject.SetActive(false); // 确保关闭
            }

            // 释放位置占用 (立即释放，以便新角色可以进入)
            if (!string.IsNullOrEmpty(runtimeState.CurrentPos))
            {
                positionOccupancy.Remove(runtimeState.CurrentPos);
                runtimeState.CurrentPos = null;
            }
        }
    }

    private IEnumerator FadeOutImage(Image img)
    {
        float elapsed = 0;
        Color c = img.color;
        float startAlpha = c.a;
        
        // 快速淡出，避免拖沓
        float duration = imageFadeInDuration; 
        
        while (elapsed < duration)
        {
            if (img == null) yield break;
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(startAlpha, 0f, elapsed / duration);
            img.color = c;
            yield return null;
        }
        
        if (img != null)
        {
            c.a = 0;
            img.color = c;
            img.gameObject.SetActive(false);
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
        var characterConfig = characterConfigs?.Find(c => c.CharacterName == characterName);
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
        Sprite targetSprite = characterConfig.GetSprite(diffIndex);
        if (targetSprite == null)
        {
            Debug.LogWarning($"角色{characterName}表情获取失败（索引 {diffIndex} 无效）");
            return;
        }

        // 获取或创建运行时状态
        if (!activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            runtimeState = new CharacterRuntimeState
            {
                CharacterName = characterName,
                CurrentImage = CreateCharacterImage(characterName, targetSprite),
                CurrentDiffIndex = diffIndex
            };
            activeCharacters[characterName] = runtimeState;
        }
        else
        {
            // 更新现有角色的表情
            if (runtimeState.CurrentImage != null)
            {
                 // 启动差分切换协程
                 if (expressionCoroutine != null) StopCoroutine(expressionCoroutine);
                 expressionCoroutine = StartCoroutine(SwitchExpressionEffect(runtimeState, targetSprite, diffIndex));
            }
        }
    }
    
    private IEnumerator SwitchExpressionEffect(CharacterRuntimeState state, Sprite targetSprite, int targetIndex)
    {
        Image img = state.CurrentImage;
        if (img == null) yield break;

        // 直接将原图切换为目标图（alpha80%）再淡入
        // 1. 直接切换图片并设置透明度
        img.sprite = targetSprite;
        state.CurrentDiffIndex = targetIndex;

        Color c = img.color;
        // 使用配置的淡入起始透明度 (默认0.8)
        float startAlpha = imageDiffFadeAlpha;
        c.a = startAlpha;
        img.color = c;

        // 2. 淡入到100%
        float elapsed = 0;
        float duration = imageDiffFadeDuration; 
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(startAlpha, 1f, elapsed / duration);
            c.a = a;
            img.color = c;
            yield return null;
        }

        c.a = 1f;
        img.color = c;
        
        expressionCoroutine = null;
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
        
        // 根据配置设置图片大小
        var config = characterConfigs?.Find(c => c.CharacterName == characterName);
        if (config != null)
        {
            image.rectTransform.sizeDelta = new Vector2(config.Width, config.Height);
        }
        else
        {
            image.rectTransform.sizeDelta = new Vector2(100, 100);
        }

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


    /// <summary>
    /// 同时设置角色位置和表情（优化：移动前切换表情，避免突兀）
    /// </summary>
    private void SetCharacterPosAndExpression(string characterName, string pos, string operation)
    {
        // 1. 获取配置和Sprite
        var characterConfig = characterConfigs?.Find(c => c.CharacterName == characterName);
        if (characterConfig == null)
        {
            Debug.LogWarning($"未找到角色配置: {characterName}");
            return;
        }

        if (!operation.StartsWith("diff")) return;
        string indexStr = operation.Substring(4);
        if (!int.TryParse(indexStr, out int diffIndex)) return;
        Sprite targetSprite = characterConfig.GetSprite(diffIndex);

        // 2. 查找位置配置
        var posConfig = characterPos.Find(p => p.pos.ToLower() == pos.ToLower());
        if (posConfig == null || posConfig.transform == null)
        {
            Debug.LogWarning($"[DialoguePanel] 未找到位置配置: {pos}");
            return;
        }

        // 3. 获取RuntimeState
        if (!activeCharacters.TryGetValue(characterName, out var runtimeState))
        {
            runtimeState = new CharacterRuntimeState
            {
                CharacterName = characterName,
                CurrentImage = CreateCharacterImage(characterName, targetSprite)
            };
            activeCharacters[characterName] = runtimeState;
        }

        Image img = runtimeState.CurrentImage;
        if (img != null)
        {
            // 4. 先切图，再切位置
            // 停止可能的淡入淡出协程
            if (expressionCoroutine != null) StopCoroutine(expressionCoroutine);
            
            // 直接设置图片于新表情
            img.sprite = targetSprite;
            // Config里有Width/Height，CreateCharacterImage已设置。
            
            runtimeState.CurrentDiffIndex = diffIndex;
            
            // 5. 切换位置
            // 处理旧位置占用
            if (!string.IsNullOrEmpty(runtimeState.CurrentPos))
            {
                positionOccupancy.Remove(runtimeState.CurrentPos);
            }
            
            img.transform.SetParent(posConfig.transform, false);
            img.transform.localPosition = Vector3.zero;
            img.transform.localScale = Vector3.one;
            
            runtimeState.CurrentPos = pos;
            positionOccupancy[pos] = characterName;
            
            // 6. 确保显示，由外部 ShowCharacter 统一调用，这里不处理淡入逻辑
        }
    }
}

