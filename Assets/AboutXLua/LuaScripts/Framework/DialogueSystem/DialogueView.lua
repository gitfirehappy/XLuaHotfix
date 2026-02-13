--- 模块类，需要桥接
local DialogueView = {}
local stringUtil = require("StringUtil")

--- UI组件引用
local uiRefs = {
    panel = nil,
    optionsParent = nil,
    characterNameText = nil,
    contentText = nil,
}

local function getController()
    return require("DialogueController")
end

---@function 初始化UI组件引用
local function InitUI()
    if uiRefs.panel then return end
    uiRefs.panel = CS.UIManager.Instance:GetForm("DialoguePanel")

    -- 绑定UI组件
    uiRefs.optionsParent = uiRefs.panel.optionsParent
    uiRefs.contentText = uiRefs.panel.contentText
    uiRefs.characterNameText = uiRefs.panel.characterNameText
    
    DialogueView.HideOptions()
end

---@function 响应点击
---@param eventData table
function DialogueView:OnPointerClick(eventData)
    CS.UnityEngine.Debug.Log("对话面板被点击，触发Next")
    getController().Next()
end

---@function 显示对话面板
function DialogueView.ShowDialogue()
    InitUI()
    CS.UIManager.Instance:ShowUIForm("DialoguePanel")
end

---@function 隐藏对话面板
function DialogueView.HideDialogue()
    if uiRefs.panel then
        CS.UIManager.Instance:HideUIForm("DialoguePanel")
        uiRefs.panel = nil
        uiRefs.optionsParent = nil
        uiRefs.contentText = nil
        uiRefs.characterNameText = nil
    end
end

---@function 更新对话
function DialogueView.UpdateDialogue(dialogueData)
    if not uiRefs.panel then return end
    
    local characterName = dialogueData.Character or ""
    local content = dialogueData.Content or ""

    local characterNames = stringUtil.SplitSemicolon(characterName)
    local posAndOps = stringUtil.SplitSemicolon(dialogueData.PosAndOp or "")

    -- 若第一个角色名存在且不为空，添加引号
    if #characterNames > 0 and characterNames[1] ~= "" then
        content = "「" .. content .. "」"
    end
    
    -- 更新文本有过渡效果，移交给C#端处理
    uiRefs.panel:SetDialogueContent(content)
    
    -- 更新角色名
    if #characterNames > 0 then
        -- 若角色名包含下划线后缀 (Role_xxxx)，UI仅显示 Role
        local dispName = characterNames[1]
        local idx = string.find(dispName, "_")
        if idx then
            dispName = string.sub(dispName, 1, idx - 1)
        end
        uiRefs.characterNameText.text = dispName
    else
        uiRefs.characterNameText.text = ""
    end
    
    CS.UnityEngine.Debug.Log("已更新角色名和内容")
    
    -- 更新角色位置和操作（允许空角色名，如;p1）
    uiRefs.panel:UpdateCharacter(characterNames, posAndOps)
    
    CS.UnityEngine.Debug.Log("已更新角色位置和操作")
end

---@function 显示选项
---@param options table
---@param callback function
function DialogueView.ShowOptions(options, callback)
    if not uiRefs.panel then return end

    -- 提取选项文本
    local optionTexts = {}
    for _, option in ipairs(options) do
        table.insert(optionTexts, option.Content or "")
    end
    
    -- 创建并显示选项
    -- C#端创建时会自动绑定传入的回调
   uiRefs.panel:CreateOptions(optionTexts, callback)
end

---@function 隐藏选项
function DialogueView.HideOptions()
    if uiRefs.optionsParent then
        -- 清空并隐藏现有选项
        uiRefs.panel:ClearOptions()
    end   
end

---@function 清理角色
function DialogueView.ClearCharacters()
    if DialogueView.uiRefs and DialogueView.uiRefs.panel then
        DialogueView.uiRefs.panel:ClearAllCharacters()
    end
end

return DialogueView
