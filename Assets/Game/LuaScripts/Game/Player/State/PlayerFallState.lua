-- 模块类，无需桥接
local PlayerHFSMState = require "PlayerHFSMState"
local FallState = setmetatable({}, {__index = PlayerHFSMState})
FallState.__index = FallState

function FallState.Create(controller)
    local obj = PlayerHFSMState.Create("Fall", controller)
    setmetatable(obj, FallState)
    return obj
end

function FallState:OnEnter(prevState)
    PlayerHFSMState.OnEnter(self, prevState)
    
    if self.anim then
        self.anim:PlayState("Airborne/Fall")
    end
end

function FallState:OnUpdate()
    -- 落地检查在AirborneState中处理
end

function FallState:OnFixedUpdate()
   
end

function FallState:OnExit()
    
end

return FallState