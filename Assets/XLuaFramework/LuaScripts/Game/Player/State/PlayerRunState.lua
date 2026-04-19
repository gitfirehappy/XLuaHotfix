-- 模块类，无需桥接
local PlayerHFSMState = require "PlayerHFSMState"
local RunState = setmetatable({}, {__index = PlayerHFSMState})
RunState.__index = RunState

function RunState.Create(controller)
    local obj = PlayerHFSMState.Create("Run", controller)
    setmetatable(obj, RunState)
    return obj
end

function RunState:OnEnter(prevState)
    PlayerHFSMState.OnEnter(self, prevState)
    
    if self.anim then
        self.anim:PlayState("Grounded/Run")
    end
end

function RunState:OnUpdate()

end

function RunState:OnFixedUpdate()

end

function RunState:OnExit()

end

return RunState