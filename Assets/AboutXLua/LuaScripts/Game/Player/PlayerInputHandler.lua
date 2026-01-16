-- 模块类,不需要桥接挂载（可选），统一管理Player输入
--- Player 输入管理器
local PlayerInputHandler = {}
PlayerInputHandler.__index = PlayerInputHandler

---@function 创建一个 PlayerInputHandler 对象
function PlayerInputHandler.Create(inputBridge)
    local obj = {}
    setmetatable(obj, PlayerInputHandler)

    if not inputBridge then
        CS.UnityEngine.Debug.LogError("[PlayerInputHandler] 传入的 InputBridge 为空！")
        return nil
    end

    obj.inputBridge = inputBridge
    obj.boundActions = {}

    -- 输入状态的存储
    obj.MoveInput = CS.UnityEngine.Vector2.zero
    -- 跨层标志变量
    obj.wantJump = false

    -- 绑定事件
    obj:BindActions()

    return obj
end

---@function 绑定所有“事件”类型的输入（如按下、松开）
function PlayerInputHandler:BindActions()
    table.insert(self.boundActions, {"Player/Jump", "started"})
    
    self.inputBridge:BindAction("Player/Jump", "started", function() self:OnJump() end)
end

---@function 解绑所有输入
function PlayerInputHandler:UnbindAll()
    if not self.inputBridge then return end

    for _, actionInfo in ipairs(self.boundActions) do
        local path, phase = actionInfo[1], actionInfo[2]
        self.inputBridge:UnbindAction(path, phase)
    end

    self.boundActions = {}
    CS.UnityEngine.Debug.Log("[PlayerInputHandler] 所有输入已解绑")
end

---@function "Jump" 事件的回调
function PlayerInputHandler:OnJump()
    self.wantJump = true
    CS.UnityEngine.Debug.Log("[PlayerInputHandler] Jump Input Received")
end

---@function 由 PlayerController 在 Update() 中调用
function PlayerInputHandler:ProcessUpdate()
    self.MoveInput = self.inputBridge:GetVector2("Player/Move")
end

---@function 由 PlayerController 在 FixedUpdate() 中调用
function PlayerInputHandler:ProcessFixedUpdate()

end

---@function 由 PlayerController 在 LateUpdate() 中调用
function PlayerInputHandler:ProcessLateUpdate()
    
end

--
-- 公共 API (供 PlayerController 和 FSM 状态调用)
--

---@function 获取移动输入
function PlayerInputHandler:GetMoveInput()
    return self.MoveInput
end

---@function 检查当前 Jump 标志变量
function PlayerInputHandler:CheckJumpFlag()
    -- CS.UnityEngine.Debug.Log("[PlayerInputHandler] CheckJumpFlag: " .. tostring(self.wantJump))
    return self.wantJump
end

---@function 清空 Jump 标志变量
function PlayerInputHandler:ClearJumpFlag()
    self.wantJump = false
    -- CS.UnityEngine.Debug.Log("[PlayerInputHandler] ClearJumpFlag" .. tostring(self.wantJump))
end

return PlayerInputHandler
