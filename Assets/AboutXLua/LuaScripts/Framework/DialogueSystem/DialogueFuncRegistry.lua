---@class DialogueFuncRegistry 对话函数注册
local DialogueFuncRegistry = {}
local stringUtil = require("StringUtil")

--- 模拟接口标记模块，用于扫描约束
DialogueFuncRegistry.IDialogueFuncProvider = {}

--- 接口元表的初始化钩子（统一执行模块init）
function DialogueFuncRegistry.IDialogueFuncProvider.__init(module)
    if type(module.init) == "function" and not module.__inited then
        module:init()
        module.__inited = true
        CS.UnityEngine.Debug.Log("[Lua-DialogueFuncRegistry] 模块"..tostring(module).."初始化元数据完成")
    end
end

function DialogueFuncRegistry.IDialogueFuncProvider:__index(key)
    return DialogueFuncRegistry.IDialogueFuncProvider[key]
end

--- 函数注册表：函数名 -> 函数
--- funcName = {func, module}
local _funcMap = {}

---@function 扫描指定模块，将模块中所有对话函数注册到对话函数注册表中，需要提前调用
---@param module table 待扫描的Lua模块
---@param moduleName string 模块名
function DialogueFuncRegistry.ScanModule(module, moduleName)
    if not IsImplementProvider(module) then
        CS.UnityEngine.Debug.LogWarning("[Lua-DialogueFuncRegistry] 模块"..moduleName.."未实现IDialogueFuncProvider接口，跳过扫描")
        return
    end
    
    -- 先完成name赋值
    DialogueFuncRegistry.IDialogueFuncProvider.__init(module)
    
    -- 扫描的函数定义时就自带名称
    for funcKey, func in pairs(module) do
        if type(func) == "function" and not string.find(tostring(funcKey), "^__") then
            -- 优先使用函数自带的name属性，无则用模块中的key作为函数名
            local funcName = func.name or funcKey
            if _funcMap[funcName] then
                CS.UnityEngine.Debug.LogWarning("[Lua-DialogueFuncRegistry] 函数名重复："..funcName.."模块名"..moduleName)
            else
                _funcMap[funcName] = {
                    func = func,
                    module = moduleName
                }
                CS.UnityEngine.Debug.Log("[Lua-DialogueFuncRegistry] 注册Lua对话函数："..funcName.."模块名"..moduleName)
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
    if not module or type(module) ~= "table" then return false end
    local mt = getmetatable(module)

    if not mt then return false end

    -- 情况1: 直接将 Interface 设为元表 (setmetatable(t, Interface))
    if mt == DialogueFuncRegistry.IDialogueFuncProvider then return true end

    -- 情况2: 标准 Lua 继承，元表的 __index 指向 Interface (setmetatable(t, {__index = Interface}))
    if mt.__index == DialogueFuncRegistry.IDialogueFuncProvider then return true end

    return false
end

return DialogueFuncRegistry