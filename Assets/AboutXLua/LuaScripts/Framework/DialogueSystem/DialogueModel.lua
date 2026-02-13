--- 模块类，无需桥接
local DialogueModel = {}
local stringUtil = require("StringUtil")

---@function 初始化对话数据
---@param data table
function DialogueModel:Init(data)
    self.currentID = "0"       -- 当前对话ID
    self.dialogueData = data   -- 整段对话配置数据
    self.isEnd = false         -- 对话是否结束
    self.optionIDs = nil       -- 选项ID列表（多个NextID时使用）
    self.jumpCount = 0         -- 跳转计数
    self.MAX_JUMP_COUNT = 100  -- 最大跳转次数

    if self.dialogueData and #self.dialogueData > 0 then
        local firstID = self.dialogueData[1].ID
        if firstID ~= "0" then
             CS.UnityEngine.Debug.LogWarning("[DialogueModel] 首条对话ID不是'0'，而是'" .. tostring(firstID) .. "'，建议使用'0'作为起始ID")
        end
        self.currentID = firstID
    else
        self.currentID = "0"
    end

    -- 用于缓存对话数据的哈希表
    self.dialogueCache = {}
    for _, dialog in ipairs(self.dialogueData) do
        self.dialogueCache[dialog.ID] = dialog
    end
end

---@function 重置跳转计数
function DialogueModel:ResetJumpCount()
    self.jumpCount = 0
end

---@function 获取当前对话数据
function DialogueModel:GetCurrentDialogue()
    if not self.dialogueCache then return nil end
    return self.dialogueCache[self.currentID]
end

---@function 检查是否为条件判断类型
function DialogueModel:IsConditionType()
    local current = self:GetCurrentDialogue()
    return current and current.Sign == "$"
end

---@function 检查是否为普通语句类型
function DialogueModel:IsNormalType()
    local current = self:GetCurrentDialogue()
    return current and current.Sign == "#"
end

---@function 获取即时执行函数（>前缀）
function DialogueModel:GetImmediateFunc()
    local current = self:GetCurrentDialogue()
    if not current then return {}, {} end

    local funcStr = current.Func or ""
    local paramStr = current.Params or ""

    local funcList = {}
    local paramList = {}

    -- 按分号分割函数和参数
    local funcs = stringUtil.SplitSemicolon(funcStr)
    local params = stringUtil.SplitSemicolon(paramStr)

    -- 过滤出>前缀的函数并匹配参数
    for i, func in ipairs(funcs) do
        if string.sub(func, 1, 1) == ">" then
            table.insert(funcList, string.sub(func, 2))  -- 移除>前缀
            -- 单个函数的参数用&分割，参数列表索引与函数对应
            local rawParams = stringUtil.SplitAmpersand(params[i] or "")
            -- 使用 StringUtil 的参数推导，将字符串参数解析为数字/表/布尔等
            table.insert(paramList, stringUtil.ParseParamList(rawParams))
        end
    end

    -- 日志
    local paramLogs = {}
    for _, _params in ipairs(paramList) do
        local parts = {}
        for _, v in ipairs(_params) do
            table.insert(parts, tostring(v))
        end
        table.insert(paramLogs, "{" .. table.concat(parts, ", ") .. "}")
    end

    CS.UnityEngine.Debug.Log("获取执行即时函数: " .. table.concat(funcList, ", ")
            .. " 参数：" .. table.concat(paramLogs, ", "))

    return funcList, paramList
end

---@function 获取交互执行函数（<前缀）
---@param data table Optional: 指定的DialogueData对象
function DialogueModel:GetInteractiveFunc(data)
    local current = data or self:GetCurrentDialogue()
    if not current then return {}, {} end

    local funcStr = current.Func or ""
    local paramStr = current.Params or ""

    local funcList = {}
    local paramList = {}

    -- 按分号分割函数和参数
    local funcs = stringUtil.SplitSemicolon(funcStr)
    local params = stringUtil.SplitSemicolon(paramStr)

    -- 过滤出<前缀的函数并匹配参数
    for i, func in ipairs(funcs) do
        if string.sub(func, 1, 1) == "<" then
            table.insert(funcList, string.sub(func, 2))  -- 移除<前缀
            -- 单个函数的参数用&分割，参数列表索引与函数对应
            local rawParams = stringUtil.SplitAmpersand(params[i] or "")
            table.insert(paramList, stringUtil.ParseParamList(rawParams))
        end
    end

    -- 日志
    local paramLogs = {}
    for _, _params in ipairs(paramList) do
        local parts = {}
        for _, v in ipairs(_params) do
            table.insert(parts, tostring(v))
        end
        table.insert(paramLogs, "{" .. table.concat(parts, ", ") .. "}")
    end

    CS.UnityEngine.Debug.Log("获取执行交互函数: " .. table.concat(funcList, ", ")
            .. " 参数：" .. table.concat(paramLogs, ", "))
    
    return funcList, paramList
end

---@function 更新当前ID（处理END和选项ID列表）
---@param nextID string
function DialogueModel:UpdateCurrentID(nextID)
    self.optionIDs = nil  -- 重置选项ID
    self.jumpCount = self.jumpCount + 1
    
    if self.jumpCount > self.MAX_JUMP_COUNT then
         CS.UnityEngine.Debug.LogError("警告: 检测到对话跳转次数过多（>" .. self.MAX_JUMP_COUNT .. "），疑似无限循环，强制结束对话")
         self.isEnd = true
         self.currentID = nil
         return
    end

    local targetNextID = tostring(nextID)
    
    if targetNextID == "END" then
        self.isEnd = true
        self.currentID = nil
    elseif targetNextID:find(";") then
        -- 多个ID视为选项
        self.optionIDs = stringUtil.SplitSemicolon(targetNextID)
    else
        self.currentID = targetNextID
    end
end

---@function 获取选项对应的对话数据列表
function DialogueModel:GetOptions()
    if not self.optionIDs then return {} end
    local options = {}
    for _, id in ipairs(self.optionIDs) do
        for _, dialog in ipairs(self.dialogueData) do
            if dialog.ID == id then
                table.insert(options, dialog)
                break
            end
        end
    end
    return options
end

---@function 清理对话数据
function DialogueModel:Cleanup()
    self.currentID = nil
    self.dialogueData = nil
    self.isEnd = false
    self.optionIDs = nil
    self.dialogueCache = nil
end

return DialogueModel