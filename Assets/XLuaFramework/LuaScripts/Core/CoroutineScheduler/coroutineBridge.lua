local Log = M.LogUtility

-- 协程系统

local coroutineBridge = {
    _coroutines = {},        -- [id] = { co = coroutine, status = "suspended/running/dead" }
    _currentCoStack = {},    -- 当前协程ID栈（支持嵌套协程）
}

--[[ 创建协程（立即返回ID，不启动）]]
function coroutineBridge.create(func)
    if type(func) ~= "function" then
        Log.Error(Log.LogLayer.Core, "coroutineBridge", "create() requires a function")
    end
    
    local id = CS.LuaCoroutineScheduler.GenerateLuaCoID() -- 生成新的协程ID

    local wrapper = function()
        -- 压入当前协程ID
        table.insert(coroutineBridge._currentCoStack, id)

        local success, err = xpcall(func, debug.traceback)
        if not success then
            Log.Error(Log.LogLayer.Core, "coroutineBridge","Coroutine error: "..tostring(err))
        end

        -- 弹出当前协程ID
        table.remove(coroutineBridge._currentCoStack)

        -- 确保完成通知
        coroutineBridge._set_status(id, "dead")
        CS.LuaCoroutineScheduler.NotifyLuaComplete(id)
    end

    coroutineBridge._coroutines[id] = {
        co = coroutine.create(wrapper),
        status = "suspended"
    }
    return id
end

--[[ 内部：设置协程状态 ]]
function coroutineBridge._set_status(id, status)
    local data = coroutineBridge._coroutines[id]
    if data then
        data.status = status
        -- 状态变更为dead时清理资源
        if status == "dead" then
            coroutineBridge._coroutines[id] = nil
            -- 清理等待关系
            CS.CoroutineBridge.CleanupWaitRelations(id)
        end
    end
end

--[[ 恢复指定协程 ]]
function coroutineBridge.resume(id)
    if id <= 0 then
        return false, "Invalid ID"
    end
    
    local data = coroutineBridge._coroutines[id]
    
    if not data then
        Log.Info(Log.LogLayer.Core, "coroutineBridge","Try to resume non-existent coroutine: "..id)
        return false, "invalid operation"
    end

    if data.status == "dead" then
        Log.Info(Log.LogLayer.Core, "coroutineBridge", string.format("尝试恢复已完成的协程 #%d", id))
        return false, "coroutine dead"
    end

    data.status = "running"
    
    Log.Info(Log.LogLayer.Core, "coroutineBridge", "恢复协程 Lua#"..id)
    
    local success, err_or_value = coroutine.resume(data.co)

    -- 正确处理恢复结果
    if not success then
        Log.Error(Log.LogLayer.Core, "coroutineBridge", "Coroutine error: "..tostring(err_or_value))
    else
        -- 打印协程状态
        local status = coroutine.status(data.co)
        Log.Info(Log.LogLayer.Core, "coroutineBridge", string.format("协程 #%d 状态: %s", id, status))
    end

    -- 更新状态
    local currentStatus = coroutine.status(data.co)
    if currentStatus == "dead" then
        Log.Info(Log.LogLayer.Core, "coroutineBridge", string.format("协程 #%d 完成", id))
        coroutineBridge._set_status(id, "dead")
    else
        data.status = "suspended"
    end

    return success
end

--[[ 停止协程（标记为终止状态）]]
function coroutineBridge.stop(id)
    local data = coroutineBridge._coroutines[id]
    if data then
        -- 标记为dead会触发清理
        coroutineBridge._set_status(id, "dead")
        return true
    end
    return false
end

--[[ 获取当前协程ID ]]
function coroutineBridge.get_current_id()
    if #coroutineBridge._currentCoStack > 0 then
        return coroutineBridge._currentCoStack[#coroutineBridge._currentCoStack]
    end
    return -1
end

--[[
    Lua协程等待C#协程完成
    @param csCoId: 要等待的C#协程ID
]]
function coroutineBridge.wait_for_csharp(csCoId)
    if type(csCoId) ~= "number" or csCoId <= 0 then
        Log.Error(Log.LogLayer.Core, "coroutineBridge","Invalid C# Coroutine ID: "..tostring(csCoId))
    end
    
    local luaCoId = coroutineBridge.get_current_id()
    
    if luaCoId < 0 then
        Log.Error(Log.LogLayer.Core, "coroutineBridge","Must be called inside a Lua coroutine")
    end
    -- 注册等待关系并挂起当前协程
    CS.CoroutineBridge.LuaWaitForCSharp(luaCoId, csCoId)
    coroutine.yield()
end

--[[
    启动C#协程
    @param func: 要执行的C#协程函数
    @return: C#协程ID
]]
function coroutineBridge.run_csharp_coroutine(func)
    if type(func) ~= "function" then
        Log.Error(Log.LogLayer.Core, "coroutineBridge","run_csharp_coroutine requires a function", 2)
    end

    -- XLua协程包装器
    local csCoId = CS.CSharpCoroutineScheduler.StartCoroutine(
            coroutineBridge._wrap_as_coroutine(func)
    )
    return csCoId
end

-- 将Lua函数包装为C#可识别的协程
function coroutineBridge._wrap_as_coroutine(func)
    return M.util.cs_generator(func)
end

return coroutineBridge