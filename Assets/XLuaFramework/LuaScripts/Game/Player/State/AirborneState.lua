-- 模块类，无需桥接
local PlayerHFSMState = require "PlayerHFSMState"
--- 空中状态
local AirborneState = setmetatable({}, {__index = PlayerHFSMState})
AirborneState.__index = AirborneState

function AirborneState.Create(controller)
    local obj = PlayerHFSMState.Create("Airborne", controller)
    setmetatable(obj, AirborneState)

    obj:InitializeSubStateMachine()
    return obj
end

function AirborneState:InitializeSubStateMachine()
    local BaseStateMachine = require "BaseStateMachine"
    self.subStateMachine = BaseStateMachine.Create()

    -- 添加空中子状态
    local PlayerJumpState = require "PlayerJumpState"
    local PlayerFallState = require "PlayerFallState"

    self.subStateMachine:AddState("Jump", PlayerJumpState.Create(self.controller))
    self.subStateMachine:AddState("Fall", PlayerFallState.Create(self.controller))
end

function AirborneState:GetInitialSubState()
    local velocityY = self.physics:GetVelocity().y

    local stateParams = self.stateMachine.stateParams or {}
    if stateParams.isJump then
        return "Jump"
    end
    
    return velocityY > 0.01 and "Jump" or "Fall"
end

function AirborneState:OnEnter(prevState)
    -- 父类方法会自动处理子状态机
    PlayerHFSMState.OnEnter(self, prevState)
end

function AirborneState:OnUpdate()
    -- 先处理子状态机
    PlayerHFSMState.OnUpdate(self)

    -- 检查是否落地
    if self.controller.isGrounded then
        self.stateMachine:ChangeState("Grounded")
        return
    end
end

function AirborneState:OnFixedUpdate()
    -- 先处理子状态机
    PlayerHFSMState.OnFixedUpdate(self)

    -- 统一空中移动控制 （可根据需要调整速度）
    local moveInput = self.inputHandler:GetMoveInput()
    local newVelocityX = moveInput.x * self.controller.playerData.moveSpeed
    local currentVelocity = self.physics:GetVelocity()

    self.physics:ApplyVelocity(CS.UnityEngine.Vector2(newVelocityX, currentVelocity.y))

    -- 统一转向处理
    self.controller:CheckFlip(moveInput.x)
end

return AirborneState