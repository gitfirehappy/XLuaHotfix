--- 常用头
--- 需要在开始调用 C#端的 ModuleRegistry.Initialize()
local M = {}
local _modules = {}
local _aliases = {}

---@function 预加载常用模块
function M.Init()
    -- 基础工具类
    -- M.util = M.require("xlua.util") -- 自带工具类可能因版本已细分
    
    -- 底层核心类
    -- M.LogUtility = M.require("LogUtility")

    -- 设置全局访问点
    _G.M = M
end

---@function 注册模块别名
function M.alias(alias, moduleName)
    _aliases[alias] = moduleName
end

---@function 获取模块（如果未加载则自动加载）
function M.require(moduleName)
    local name = _aliases[moduleName] or moduleName

    if not _modules[name] then
        _modules[name] = require(name)
    end

    return _modules[name]
end

---@function 获取已加载模块（不自动加载）
function M.get(moduleName)
    local name = _aliases[moduleName] or moduleName
    return _modules[name]
end

---@function 检查模块是否已加载
function M.has(moduleName)
    return M.get(moduleName) ~= nil
end

---@function 清空缓存（热重载用）
function M.clear()
    _modules = {}
end

--- 设置全局访问点
_G.M = M

return M