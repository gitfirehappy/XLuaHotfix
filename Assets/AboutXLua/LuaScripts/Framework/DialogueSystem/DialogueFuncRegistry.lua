---@class DialogueFuncRegistry 对话函数注册
local DialogueFuncRegistry = {}

--- 模拟接口标记模块，用于扫描约束
DialogueFuncRegistry.IDialogueFuncProvider = {}

--- 函数注册表：函数名 -> 函数
--- funcName = {func, module, paramTypes}
--- paramTypes 为可选的参数类型数组，用于参数适配：{"string", "number", "boolean", "table"}
local _funcMap = {}

---@function 将值适配为期望类型
---@param value any 待适配的值
---@param expectedType string 期望类型："string", "number", "boolean", "table", nil表示不转换
local function AdaptValue(value, expectedType)
    if value == nil or expectedType == nil then
        return value
    end

    local currentType = type(value)
    
    -- 如果类型已匹配，直接返回
    if currentType == expectedType then
        return value
    end

    -- 期望string类型，将任意类型转换为字符串
    if expectedType == "string" then
        return tostring(value)
    end
    
    -- 期望number类型，尝试转换
    if expectedType == "number" then
        if currentType == "string" then
            return tonumber(value) or value
        end
        return tonumber(value) or value
    end
    
    -- 期望boolean类型
    if expectedType == "boolean" then
        if currentType == "string" then
            local lower = string.lower(value)
            if lower == "true" or lower == "1" then return true end
            if lower == "false" or lower == "0" then return false end
        end
        if currentType == "number" then
            return value ~= 0
        end
        return value and true or false
    end
    
    -- 期望table类型，暂不转换
    if expectedType == "table" then
        return value
    end
    
    return value
end

---@function 参数适配：根据期望的参数类型列表修正推导的参数类型
---@param params table 参数列表
---@param paramTypes table|nil 期望的参数类型列表
local function AdaptParameters(params, paramTypes)
    if not params or not paramTypes then
        return params
    end
    
    local adapted = {}
    for i, value in ipairs(params) do
        local expectedType = paramTypes[i]
        adapted[i] = AdaptValue(value, expectedType)
    end
    return adapted
end

---@function 扫描实现 IDialogueFuncProvider 的模块
---@param module table
---@param moduleName string
function DialogueFuncRegistry.ScanModule(module, moduleName)
    if not IsImplementProvider(module) then
        CS.UnityEngine.Debug.LogWarning(("[Lua] 模块 %s 未实现 IDialogueFuncProvider 接口，跳过扫描"):format(moduleName))
        return
    end

    for key, item in pairs(module) do
        -- 跳过元字段（__开头）和非表类型
        if type(key) == "string" and not key:find("^__") and type(item) == "table" then
            -- 严格校验：必须含 name(string) + func(function)
            if type(item.name) == "string" and type(item.func) == "function" then
                local funcName = item.name
                if _funcMap[funcName] then
                    CS.UnityEngine.Debug.LogWarning(("[Lua] 函数名冲突: %s (原模块:%s, 新模块:%s)"):format(
                            funcName, _funcMap[funcName].module, moduleName))
                else
                    -- paramTypes 为可选的参数类型数组
                    _funcMap[funcName] = { 
                        func = item.func, 
                        module = moduleName,
                        paramTypes = item.paramTypes  -- 可选：期望的参数类型列表
                    }
                    CS.UnityEngine.Debug.Log(("[Lua] 注册函数: %s (模块:%s)"):format(funcName, moduleName))
                end
            end
        end
    end
end

---@function 调用Lua对话函数
---@param funcName string 函数名
---@param params table 入参列表
function DialogueFuncRegistry.InvokeFunction(funcName, params)
    local funcInfo = _funcMap[funcName]
    if not funcInfo then
        CS.UnityEngine.Debug.LogError("[Lua-DialogueFuncRegistry] 未找到Lua对话函数："..funcName)
        return nil
    end
    
    params = params or {}
    -- 参数适配：根据期望的参数类型修正推导的参数类型
    local adaptedParams = AdaptParameters(params, funcInfo.paramTypes)
    
    local status, result = pcall(funcInfo.func, table.unpack(adaptedParams))
    if not status then
        CS.UnityEngine.Debug.LogError("[Lua-DialogueFuncRegistry] 执行Lua函数"..funcName.."出错:"..result)
        return nil
    end
    return result
end

---@function 取消注册对话函数
---@param moduleName string 要取消注册的模块名
function DialogueFuncRegistry.UnregisterFunction(moduleName)
    for funcName, funcInfo in pairs(_funcMap) do
        if funcInfo.module == moduleName then
            _funcMap[funcName] = nil
        end
    end
end

---@function 检查模块是否实现IDialogueFuncProvider接口（元表判断）
---@param module table 待检查的Lua模块 
function IsImplementProvider(module)
    local mt = getmetatable(module)
    return mt and mt.__index == DialogueFuncRegistry.IDialogueFuncProvider
end

return DialogueFuncRegistry