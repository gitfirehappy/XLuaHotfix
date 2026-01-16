--- 模块类，无需桥接
local DialogueController = {}

local view = require("DialogueView")
local model = require("DialogueModel")
local dataManager = require("DialogueDataManager")
local dialogueFuncRegistry = require("DialogueFuncRegistry")
local currentDialogueFile = nil

---@function 开始对话
---@param fileName string 文件名（lua模块名）
function DialogueController.Start(fileName)
    currentDialogueFile = fileName

    local dialogueData = dataManager.LoadDialogueData(fileName)
    if not dialogueData then
        CS.UnityEngine.Debug.LogError("Load dialogue data failed: " .. fileName)
        return
    end

    model:Init(dialogueData)

    view.ShowDialogue()
    DialogueController.Refresh()

    CS.UnityEngine.Debug.Log("[Lua-DialogueController]Start dialogue: " .. fileName)
end

---@function 刷新对话（核心流程）
function DialogueController.Refresh()
    if model.isEnd then
        DialogueController.End()
        return
    end
    
    view.HideOptions()

    local currentDialogue = model:GetCurrentDialogue()
    if not currentDialogue then
        DialogueController.End()
        return
    end

    -- 执行即时函数列表（>前缀）
    local funcList, paramList = model:GetImmediateFunc()
    if #funcList > 0 then
        local conditionResult  -- 条件判断仅使用第一个函数的返回值
        for i, funcName in ipairs(funcList) do
            local params = paramList[i] or {}
            -- 按顺序执行函数
            local result = DialogueController.Execute(funcName, params)
            -- 记录第一个函数的返回值作为条件判断结果
            if i == 1 then
                conditionResult = result
            end
        end

        -- 处理条件判断返回值（仅条件类型需要）
        if model:IsConditionType() then
            CS.UnityEngine.Debug.Log("条件判断类型，结果: " .. tostring(conditionResult))
            if conditionResult then
                DialogueController.Next(conditionResult)
                return  -- 重要：条件判断跳转后直接返回，避免重复执行
            else
                CS.UnityEngine.Debug.LogError("条件判断返回空值，无法继续")
                DialogueController.End()
                return
            end
        end
    end

    -- 显示选项或普通对话
    local options = model:GetOptions()
    if #options > 0 then
        view.ShowOptions(options, DialogueController.OnOptionSelect)
    else
        view.UpdateDialogue(currentDialogue)
    end
end

---@function 下一条对话（平台对话点击面板回调，选项和条件判断跳转调用）
---@param nextID string 下一条ID
function DialogueController.Next(nextID)
    local currentDialogue = model:GetCurrentDialogue()
    if not currentDialogue then return end

    -- 执行交互函数列表（<前缀）
    local funcList, paramList = model:GetInteractiveFunc()
    if #funcList > 0 then
        for i, funcName in ipairs(funcList) do
            local params = paramList[i] or {}
            -- 按顺序执行函数
            DialogueController.Execute(funcName, params)
        end
    end

    -- 更新当前ID（自动处理选项ID列表）
    local targetNextID = nextID or currentDialogue.NextID or "END"
    model:UpdateCurrentID(targetNextID)
    DialogueController.Refresh()
    
    CS.UnityEngine.Debug.Log("Next dialogue: " .. targetNextID)
end

---@function 选项选中回调，在view层绑定至按钮
---@param optionIndex number 选项索引
function DialogueController.OnOptionSelect(optionIndex)
    local options = model:GetOptions()
    if optionIndex and options[optionIndex] then -- 默认从1开始，可根据需求调整
        local selectedOption = options[optionIndex]
        -- 执行选项对应的跳转
        DialogueController.Next(selectedOption.NextID)
    end
end

---@function 执行注册的函数
---@param funcName string 函数名
---@param params table 参数
function DialogueController.Execute(funcName, params)
    CS.UnityEngine.Debug.Log("Execute function: " .. funcName)
    -- 调用注册的函数
    return dialogueFuncRegistry.InvokeFunction(funcName, params)
end

---@function 结束对话
function DialogueController.End()
    CS.UnityEngine.Debug.Log("End dialogue: " .. (currentDialogueFile or ""))
    view.HideOptions()
    view.HideDialogue()
    model:Cleanup()
    currentDialogueFile = nil
    -- 可选：卸载文件
end

return DialogueController