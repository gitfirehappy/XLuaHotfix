-- 模块类，无需桥接
--- 状态机基类
local BaseStateMachine = {}
BaseStateMachine.__index = BaseStateMachine

---@function 创建状态机
function BaseStateMachine.Create(initialState)
    local obj = {}
    setmetatable(obj, BaseStateMachine)
    
    obj.states = {}         -- 所有状态实例
    obj.currentState = nil  -- 当前状态
    obj.prevState = nil     -- 上一状态
    obj.stateParams = {}    -- 状态参数
    obj.parentStateMachine = nil    -- 父状态机 (用于HFSM)
    obj.isSubStateMachine = false   -- 是否属于子状态机

    if initialState then
        obj:ChangeState(initialState)
    end
    
    return obj
end 

---@function 添加状态
function BaseStateMachine:AddState(stateName, stateInstance)
    if not stateName or not stateInstance then return end
    self.states[stateName] = stateInstance
    stateInstance.stateMachine = self  -- 反向绑定

    if stateInstance.subStateMachine then
        stateInstance.subStateMachine.parentStateMachine = self
        stateInstance.subStateMachine.isSubStateMachine = true
    end
end 

---@function 切换状态
function BaseStateMachine:ChangeState(newStateName, params)
    local newState = self.states[newStateName]
    
    if self.currentState and self.currentState.Name == newStateName then
        return -- 相同状态不切换
    end

    local prevState = self.currentState
    self.prevState = prevState
    self.stateParams = params or {}

    -- 退出当前状态
    if prevState then
        if prevState.OnExit then
            prevState:OnExit(newStateName)
        end

        -- 如果有子状态机，退出子状态机
        if prevState.subStateMachine then
            prevState.subStateMachine:ChangeState(nil)
        end
    end
    
    self.currentState = newState
    
    -- 进入新状态
    if newState then
        if newState.OnEnter then
            newState:OnEnter(prevState)
        end
    end
end

---@function 返回之前状态
function BaseStateMachine:ReturnToPreviousState()
    if self.prevState then
        self:ChangeState(self.prevState.Name)
    end
end

---@function 更新当前状态
function BaseStateMachine:ProcessUpdate()
    if self.currentState then
        -- 先更新当前状态的子状态机
        if self.currentState.subStateMachine then
            self.currentState.subStateMachine:ProcessUpdate()
        end

        -- 再更新当前状态
        if self.currentState.OnUpdate then
            self.currentState:OnUpdate()
        end
    end
end

---@function 固定更新
function BaseStateMachine:ProcessFixedUpdate()
    if self.currentState then
        -- 先更新当前状态的子状态机
        if self.currentState.subStateMachine then
            self.currentState.subStateMachine:ProcessFixedUpdate()
        end

        -- 再更新当前状态
        if self.currentState.OnFixedUpdate then
            self.currentState:OnFixedUpdate()
        end
    end
end

---@function 获取当前状态名
function BaseStateMachine:GetCurrentStateName()
    return self.currentState and self.currentState.Name or "None"
end

---@function 检查是否在某个状态
function BaseStateMachine:IsInState(stateName)
    return self.currentState and self.currentState.Name == stateName
end

---@function 清理
function BaseStateMachine:Cleanup()
    -- 退出当前状态（不带任何新状态）
    self:ChangeState(nil)

    -- 清理所有状态实例的引用
    if self.states then
        for stateName, state in pairs(self.states) do
            -- 递归清理子状态机 (如果它们也有 Cleanup)
            if state.subStateMachine and state.subStateMachine.Cleanup then
                state.subStateMachine:Cleanup()
            end
            state.stateMachine = nil
        end
    end

    self.states = {}
    self.currentState = nil
    self.prevState = nil
    self.parentStateMachine = nil
end

return BaseStateMachine