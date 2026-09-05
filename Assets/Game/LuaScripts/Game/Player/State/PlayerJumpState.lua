-- 模块类，无需桥接
local PlayerHFSMState = require "PlayerHFSMState"
local JumpState = setmetatable({}, {__index = PlayerHFSMState})
JumpState.__index = JumpState

function JumpState.Create(controller)
    local obj = PlayerHFSMState.Create("Jump", controller)
    setmetatable(obj, JumpState)
    return obj
end

function JumpState:OnEnter(prevState)
    PlayerHFSMState.OnEnter(self, prevState)
    
    if self.anim then
        self.anim:PlayState("Airborne/Jump")
    end

    self.physics:ApplyImpulse(CS.UnityEngine.Vector2.up, self.controller.playerData.jumpForce)

    CS.UnityEngine.Debug.Log("[PlayerJumpState] 进入跳跃状态，应用跳跃力: " .. self.controller.playerData.jumpForce)
end

function JumpState:OnUpdate()
    local velocityY = self.physics:GetVelocity().y
    
    -- 检查是否开始下落
    if velocityY <= 0 then
        self.stateMachine:ChangeState("Fall")
        return
    end
end

function JumpState:OnFixedUpdate()
    
end

function JumpState:OnExit()
    
end

return JumpState