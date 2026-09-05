-- 模块类，无需桥接
local BaseState = require "BaseState"
local PlayerHFSMState = setmetatable({}, {__index = BaseState})
PlayerHFSMState.__index = PlayerHFSMState

function PlayerHFSMState.Create(name, controller)
    local obj = BaseState.Create(name)
    setmetatable(obj, PlayerHFSMState)

    obj.controller = controller
    obj.anim = controller.anim
    obj.inputHandler = controller.inputHandler
    obj.physics = controller.physics
    obj.subStateMachine = nil   -- 子状态机

    return obj
end

-- 复合状态可以重写这些方法来初始化子状态机
function PlayerHFSMState:InitializeSubStateMachine()
    -- 默认不初始化子状态机（叶子状态）
end

function PlayerHFSMState:GetInitialSubState()
    -- 复合状态需要重写此方法
    return nil
end

-- 生命周期方法

function PlayerHFSMState:OnEnter(prevState)
    -- 如果是复合状态，初始化子状态机
    if self.subStateMachine then
        local initialState = self:GetInitialSubState()
        if initialState then
            self.subStateMachine:ChangeState(initialState)
        end
    end
end

function PlayerHFSMState:OnUpdate()
    if self.subStateMachine then
        self.subStateMachine:ProcessUpdate()
    end
end

function PlayerHFSMState:OnFixedUpdate()
    if self.subStateMachine then
        self.subStateMachine:ProcessFixedUpdate()
    end
end

function PlayerHFSMState:OnExit(nextState)
    if self.subStateMachine then
        self.subStateMachine:ChangeState(nil)
    end
end

-- 工具方法
function PlayerHFSMState:ChangeTopState(stateName, params)
    -- 切换到顶层状态 - 通过控制器访问顶层状态机
    if self.controller and self.controller.stateMachine then
        self.controller.stateMachine:ChangeState(stateName, params)
    end
end

function PlayerHFSMState:ChangeSubState(stateName, params)
    -- 切换到子状态（仅复合状态可用）
    if self.subStateMachine then
        self.subStateMachine:ChangeState(stateName, params)
    end
end

-- 获取顶层状态机
function PlayerHFSMState:GetTopStateMachine()
    return self.controller and self.controller.stateMachine
end

return PlayerHFSMState
