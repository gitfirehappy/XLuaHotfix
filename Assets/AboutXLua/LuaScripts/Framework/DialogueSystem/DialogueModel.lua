--- 模块类，无需桥接
local DialogueModel = {}
local stringUtil = require("StringUtil")

---@function 格式化参数用于日志输出
---@param value any 要格式化的值
---@return string 格式化后的字符串
local function FormatValueForLog(value)
    if type(value) == "nil" then
        return "nil"
    elseif type(value) == "string" then
        return '"' .. value .. '"'
    elseif type(value) == "number" or type(value) == "boolean" then
        return tostring(value)
    elseif type(value) == "table" then
        local items = {}
        local hasKeys = false
        local hasArray = false
        
        -- 检查是否有数组元素
        for i = 1, #value do
            if value[i] ~= nil then
                hasArray = true
                break
            end
        end
        
        -- 检查是否有键值对
        for k, v in pairs(value) do
            if type(k) ~= "number" or k > #value then
                hasKeys = true
                break
            end
        end
        
        if hasArray and not hasKeys then
            -- 纯数组
            for i = 1, #value do
                table.insert(items, FormatValueForLog(value[i]))
            end
            return "[" .. table.concat(items, ",") .. "]"
        elseif hasKeys and not hasArray then
            -- 纯哈希表
            for k, v in pairs(value) do
                table.insert(items, k .. "=" .. FormatValueForLog(v))
            end
            return "{" .. table.concat(items, ",") .. "}"
        else
            -- 混合表
            for i = 1, #value do
                table.insert(items, FormatValueForLog(value[i]))
            end
            for k, v in pairs(value) do
                if type(k) ~= "number" or k > #value then
                    table.insert(items, k .. "=" .. FormatValueForLog(v))
                end
            end
            return "{" .. table.concat(items, ",") .. "}"
        end
    else
        return tostring(value)
    end
end

---@function 初始化对话数据
---@param data table
function DialogueModel:Init(data)
    self.currentID = "0"       -- 当前对话ID
    self.dialogueData = data   -- 整段对话配置数据
    self.isEnd = false         -- 对话是否结束
    self.optionIDs = nil       -- 选项ID列表（多个NextID时使用）

    -- 用于缓存对话数据的哈希表
    self.dialogueCache = {}
    for _, dialog in ipairs(self.dialogueData) do
        self.dialogueCache[dialog.ID] = dialog
    end

    -- 用于跟踪已访问过的ID，防止无限循环
    self.visitedIDs = {}
end

---@function 获取当前对话数据
function DialogueModel:GetCurrentDialogue()
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
            table.insert(paramList, stringUtil.SplitAmpersand(params[i] or ""))
        end
    end

    local paramLogs = {}
    for _, _params in ipairs(paramList) do
        local formattedParams = {}
        for _, param in ipairs(_params) do
            table.insert(formattedParams, FormatValueForLog(param))
        end
        table.insert(paramLogs, "{" .. table.concat(formattedParams, ", ") .. "}")
    end

    CS.UnityEngine.Debug.Log("获取执行即时函数: " .. table.concat(funcList, ", ")
            .. " 参数：" .. table.concat(paramLogs, ", "))

    return funcList, paramList
end

---@function 获取交互执行函数（<前缀）
function DialogueModel:GetInteractiveFunc()
    local current = self:GetCurrentDialogue()
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
            table.insert(paramList, stringUtil.SplitAmpersand(params[i] or ""))
        end
    end

    local paramLogs = {}
    for _, _params in ipairs(paramList) do
        local formattedParams = {}
        for _, param in ipairs(_params) do
            table.insert(formattedParams, FormatValueForLog(param))
        end
        table.insert(paramLogs, "{" .. table.concat(formattedParams, ", ") .. "}")
    end

    CS.UnityEngine.Debug.Log("获取执行交互函数: " .. table.concat(funcList, ", ")
            .. " 参数：" .. table.concat(paramLogs, ", "))
    
    return funcList, paramList
end

---@function 更新当前ID（处理END和选项ID列表）
---@param nextID string
function DialogueModel:UpdateCurrentID(nextID)
    self.optionIDs = nil  -- 重置选项ID
    if nextID == "END" then
        self.isEnd = true
        self.currentID = nil
    elseif nextID:find(";") then
        -- 多个ID视为选项
        self.optionIDs = stringUtil.SplitSemicolon(nextID)
    else
        -- 检测是否进入无限循环
        if self.visitedIDs[nextID] then
            CS.UnityEngine.Debug.LogError("警告: 检测到对话ID " .. nextID .. " 出现循环引用，强制结束对话")
            self.isEnd = true
            self.currentID = nil
            return
        end

        -- 记录访问过的ID
        self.visitedIDs[nextID] = true
        self.currentID = nextID
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
end

return DialogueModel