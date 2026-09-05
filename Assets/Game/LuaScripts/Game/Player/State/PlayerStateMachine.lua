-- 模块类，无需桥接
local BaseStateMachine = require "BaseStateMachine"
local PlayerStateMachine = setmetatable({}, {__index = BaseStateMachine})
PlayerStateMachine.__index = PlayerStateMachine

function PlayerStateMachine.Create(controller)
    local obj = BaseStateMachine.Create()
    setmetatable(obj, PlayerStateMachine)

    obj.controller = controller
    obj.animBridge = controller.anim

    return obj
end

function PlayerStateMachine:CanMove()
    return not self:IsInState("Dialogue") and not self:IsInState("Disabled")
end

function PlayerStateMachine:IsGrounded()
    return self.controller.isGrounded
end

function PlayerStateMachine:Cleanup()
    if not self.states then return end

    for stateName, state in pairs(self.states) do
        -- 递归清理子状态机
        if state.subStateMachine then
            state.subStateMachine:Cleanup()
            state.subStateMachine = nil
        end

        -- 清理状态引用
        state.stateMachine = nil
        state.controller = nil
        state.anim = nil
        state.inputHandler = nil
        state.physics = nil
    end

    self.states = {}
    self.currentState = nil
    self.prevState = nil
    self.stateParams = {}

    CS.UnityEngine.Debug.Log("[PlayerStateMachine] 状态机已清理")
end

return PlayerStateMachine