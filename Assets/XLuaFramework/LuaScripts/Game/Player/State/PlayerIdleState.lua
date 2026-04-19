-- 模块类，无需桥接
local PlayerHFSMState = require "PlayerHFSMState"
local IdleState = setmetatable({}, {__index = PlayerHFSMState})
IdleState.__index = IdleState

function IdleState.Create(controller)
    local obj = PlayerHFSMState.Create("Idle", controller)
    setmetatable(obj, IdleState)
    return obj
end

function IdleState:OnEnter(prevState)
    PlayerHFSMState.OnEnter(self, prevState)
    
    if self.anim then
        self.anim:PlayState("Grounded/Idle")
    end
end

function IdleState:OnUpdate()

end

function IdleState:OnFixedUpdate()
    
end

function IdleState:OnExit()
    
end

return IdleState
