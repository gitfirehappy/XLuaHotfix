-- 模块类，无需桥接
local PlayerHFSMState = require "PlayerHFSMState"
local GroundedState = setmetatable({}, {__index = PlayerHFSMState})
GroundedState.__index = GroundedState

function GroundedState.Create(controller)
    local obj = PlayerHFSMState.Create("Grounded", controller)
    setmetatable(obj, GroundedState)

    obj:InitializeSubStateMachine()
    return obj
end

function GroundedState:InitializeSubStateMachine()
    local BaseStateMachine = require "BaseStateMachine"
    self.subStateMachine = BaseStateMachine.Create()

    -- 添加地面子状态
    local PlayerIdleState = require "PlayerIdleState"
    local PlayerRunState = require "PlayerRunState"

    self.subStateMachine:AddState("Idle", PlayerIdleState.Create(self.controller))
    self.subStateMachine:AddState("Run", PlayerRunState.Create(self.controller))
end

function GroundedState:GetInitialSubState()
    if not self.inputHandler then
        return "Idle"
    end
    
    local moveInput = self.inputHandler:GetMoveInput()
    return math.abs(moveInput.x) > 0.1 and "Run" or "Idle"
end

function GroundedState:OnEnter(prevState)
    -- 父类方法会自动处理子状态机
    PlayerHFSMState.OnEnter(self)

    if self.inputHandler then
        self.inputHandler:ClearJumpFlag()
    end
end

function GroundedState:OnUpdate()
    -- 1.先处理子状态机
    PlayerHFSMState.OnUpdate(self)

    -- 2.检查是否离开地面
    if not self.controller.isGrounded then
        self.stateMachine:ChangeState("Airborne")
        return
    end

    -- 3.检查是否跳跃
    if self.inputHandler:CheckJumpFlag() then
        self.inputHandler:ClearJumpFlag()
        self:ChangeTopState("Airborne",{isJump = true})
        return
    end

    -- 4. 处理子状态之间的切换
    local moveInput = self.inputHandler:GetMoveInput()
    local currentStateName = self.subStateMachine:GetCurrentStateName()

    if currentStateName == "Idle" then
        if math.abs(moveInput.x) > 0.01 then
            self:ChangeSubState("Run")
            return
        end
    elseif currentStateName == "Run" then
        if math.abs(moveInput.x) < 0.01 then
            self:ChangeSubState("Idle")
            return
        end
    end
    
end

function GroundedState:OnFixedUpdate()
    -- 先处理子状态机
    PlayerHFSMState.OnFixedUpdate(self)
    
    -- 处理运动通用转向,移动写到子状态机有问题
    local moveInput = self.inputHandler:GetMoveInput()
    local newVelocityX = moveInput.x * self.controller.playerData.moveSpeed
    local currentVelocity = self.physics:GetVelocity()
    self.physics:ApplyVelocity(CS.UnityEngine.Vector2(newVelocityX, currentVelocity.y))
    
    self.controller:CheckFlip(moveInput.x)
end

return GroundedState