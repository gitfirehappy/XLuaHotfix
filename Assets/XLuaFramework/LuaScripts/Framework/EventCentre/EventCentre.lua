-- 事件中心：用于Lua与Lua、Lua与C#之间的通信

local EventCentre = {}

-- 将C#枚举映射到Lua
EventCentre.Port = {
    CsharpToCsharp = 0,
    LuaToLua = 1,
    LuaToCsharp = 2,
    CsharpToLua = 3
}

-- 事件处理器表：存储结构为 {handler = 原始回调, closure = 注册到C#的闭包}
local eventHandlers = {}

function EventCentre.init()
    print("EventCentre.lua initialized")
    for port = 0, 3 do
        eventHandlers[port] = {}
    end
end

-- 注册事件：保存闭包用于取消注册
function EventCentre.on(port, eventName, handler)
    if not eventHandlers[port] then
        eventHandlers[port] = {}
    end
    if not eventHandlers[port][eventName] then
        eventHandlers[port][eventName] = {}
    end

    local closure = nil
    -- 非LuaToLua事件需要注册到C#，创建闭包并保存
    if port ~= EventCentre.Port.LuaToLua then
        closure = function(...)
            local args = {...}
            if #args > 0 then
                handler(unpack(args))
            else
                handler()
            end
        end
        CS.EventCentre.RegisterLuaEvent(port, eventName, closure)
    end

    -- 存储原始handler和闭包的映射
    table.insert(eventHandlers[port][eventName], {
        handler = handler,
        closure = closure
    })
end

-- 取消注册事件：使用保存的闭包移除C#端委托
function EventCentre.off(port, eventName, handler)
    if not eventHandlers[port] or not eventHandlers[port][eventName] then
        return
    end

    for i = #eventHandlers[port][eventName], 1, -1 do
        local item = eventHandlers[port][eventName][i]
        if item.handler == handler then
            -- 移除C#端注册的闭包
            if port ~= EventCentre.Port.LuaToLua and item.closure then
                CS.EventCentre.UnregisterLuaEvent(port, eventName, item.closure)
            end
            -- 移除Lua端存储
            table.remove(eventHandlers[port][eventName], i)
            break
        end
    end
end

-- 触发事件
function EventCentre.trigger(port, eventName, ...)
    local args = {...}

    -- 触发Lua到Lua事件
    if port == EventCentre.Port.LuaToLua then
        if eventHandlers[port] and eventHandlers[port][eventName] then
            for _, handler in ipairs(eventHandlers[port][eventName]) do
                if #args > 0 then
                    handler(unpack(args))
                else
                    handler()
                end
            end
        end
    else
        -- 触发其他类型事件（通过C#事件中心）
        if #args > 0 then
            CS.EventCentre.TriggerEventFromLua(port, eventName, args[1])
        else
            CS.EventCentre.TriggerEventFromLua(port, eventName)
        end
    end
end

--[[
    实用方法
]]

-- 检查事件是否存在
function EventCentre.has(port, eventName)
    return eventHandlers[port] and eventHandlers[port][eventName] and #eventHandlers[port][eventName] > 0
end

-- 获取事件监听器数量
function EventCentre.count(port, eventName)
    if not eventHandlers[port] or not eventHandlers[port][eventName] then
        return 0
    end
    return #eventHandlers[port][eventName]
end

-- 清除所有事件
function EventCentre.clear_all()
    for port = 0, 3 do
        eventHandlers[port] = {}
    end
    -- 还需要清除C#端注册的事件
    CS.EventCentre.ClearAllEvents()
end

return EventCentre