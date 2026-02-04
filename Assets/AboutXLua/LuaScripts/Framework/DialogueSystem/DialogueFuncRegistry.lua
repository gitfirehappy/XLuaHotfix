---@class DialogueFuncRegistry 对话函数注册
local DialogueFuncRegistry = {}

--- 模拟接口标记模块，用于扫描约束
DialogueFuncRegistry.IDialogueFuncProvider = {}

--- 函数注册表：函数名 -> 函数
--- funcName = {func, module}
local _funcMap = {}

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
                    _funcMap[funcName] = { func = item.func, module = moduleName }
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
    local status, result = pcall(funcInfo.func, table.unpack(params))
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