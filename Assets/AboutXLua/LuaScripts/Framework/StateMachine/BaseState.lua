-- 模块类，无需桥接
--- 状态基类
local BaseState = {}
BaseState.__index = BaseState

---@function 创建状态
function BaseState.Create(name)
    local obj = {}
    setmetatable(obj, BaseState)
    
    obj.Name = name or "UnnamedState"
    obj.stateMachine = nil
    
    return obj
end

-- 以下方法需子类重写

---@function 状态进入
function BaseState:OnEnter(prevState)
    
end

---@function 状态更新
function BaseState:OnUpdate()
    
end

---@function 状态固定更新
function BaseState:OnFixedUpdate()
    
end

---@function 退出状态
function BaseState:OnExit(nextState)
    
end

return BaseState