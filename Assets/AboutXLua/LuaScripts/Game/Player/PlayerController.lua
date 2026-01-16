-- 挂载脚本类，需要桥接
--- Player主控制器
local PlayerController = {}
local PlayerInputHandler = require "PlayerInputHandler"

function PlayerController.New(go)
    local obj = {}
    setmetatable(obj, { __index = PlayerController })
    obj.gameObject = go
    obj.transform = go.transform
    
    -- Bridge组件
    obj.physics = go:GetComponent("Physics2DBridge")
    obj.collision = go:GetComponent("Collision2DBridge")
    obj.input = go:GetComponent("InputBridge")
    obj.gizmos = go:GetComponent("GizmosBridge")
    obj.so = go:GetComponent("ScriptObjectBridge")
    obj.anim = go:GetComponent("AnimBridge")

    -- 从SO加载玩家属性
    obj.playerData = obj.so:GetSO("PlayerControllerSO")
    if not obj.playerData then
        CS.UnityEngine.Debug.LogError("[PlayerController] 无法获取PlayerData配置！")
    else
        CS.UnityEngine.Debug.Log("[PlayerController] 成功加载PlayerData配置")
    end
    
    obj.isGrounded = false
    obj.facingRight = true
    
    -- 初始化状态机
    obj.stateMachine = nil

    return obj
end

function PlayerController:Awake()
    -- 从SO设置重力
    if self.playerData then
        self.physics:SetGravityScale(self.playerData.gravityScale)
    end

    -- 创建输入处理
    self.inputHandler = PlayerInputHandler.Create(self.input)
    
    CS.UnityEngine.Debug.Log("[PlayerController] Awake")
end


function PlayerController:Start()
    -- 初始化状态机
    self:InitializeStateMachine()
end

---@function 初始化状态机
function PlayerController:InitializeStateMachine()
    -- 引入状态机和所有状态
    local PlayerStateMachine = require "PlayerStateMachine"
    local GroundedState = require "GroundedState"
    local AirborneState = require "AirborneState"

    -- 创建顶层状态机实例
    self.stateMachine = PlayerStateMachine.Create(self)

    -- 创建HFSM状态实例
    local groundedState = GroundedState.Create(self)
    local airborneState = AirborneState.Create(self)

    -- 将状态添加到状态机
    self.stateMachine:AddState("Grounded", groundedState)
    self.stateMachine:AddState("Airborne", airborneState)

    -- 设置初始状态
    local initialState = self.isGrounded and "Grounded" or "Airborne"
    self.stateMachine:ChangeState(initialState)

    CS.UnityEngine.Debug.Log("状态机初始化完成")
end

function PlayerController:Update()
    if self.inputHandler then
        self.inputHandler:ProcessUpdate()
    end
    
    -- 驱动状态机Update
    if self.stateMachine then
        self.stateMachine:ProcessUpdate()
    end
end

function PlayerController:FixedUpdate()
    -- 1. 先处理输入（物理更新前）
    if self.inputHandler then
        self.inputHandler:ProcessFixedUpdate()
    end
    
    -- 2. 检查物理状态
    self:GroundCheck()

    -- 3. 驱动状态机FixedUpdate
    if self.stateMachine then
        self.stateMachine:ProcessFixedUpdate()
    end
end

function PlayerController:OnDestroy()
    CS.UnityEngine.Debug.Log("[PlayerController] OnDestroy")

    -- 清理输入
    if self.inputHandler then
        self.inputHandler:UnbindAll()
        self.inputHandler = nil
    end

    -- 清理状态机
    if self.stateMachine then
        self.stateMachine:Cleanup()
        self.stateMachine = nil
    end
end

---@function 运动通用转向
function PlayerController:CheckFlip(moveInputX)
    if moveInputX > 0.1 and not self.facingRight then
        self:Flip()
    elseif moveInputX < -0.1 and self.facingRight then
        self:Flip()
    end
end

-- 辅助方法

---@function 翻转
function PlayerController:Flip()
    self.facingRight = not self.facingRight
    local scale = self.transform.localScale
    self.transform.localScale = CS.UnityEngine.Vector3(-scale.x, scale.y, scale.z)
end

---@function 地面检测
function PlayerController:GroundCheck()
    local colliders = self.physics:OverlapCircleAll(
            self.playerData.groundCheckOffset,  -- 检测位置偏移
            self.playerData.groundCheckRadius,  -- 检测半径
            "Ground"           -- 地面层
    )
    self.isGrounded = colliders.Length > 0
end

function PlayerController:OnDrawGizmos()
    -- 计算地面检测的世界坐标
    local checkPosition = self.transform.position + CS.UnityEngine.Vector3(
            self.playerData.groundCheckOffset.x, 
            self.playerData.groundCheckOffset.y, 
            0
    )

    -- 根据是否接地设置不同颜色（接地绿色，未接地红色）
    local color = self.isGrounded and CS.UnityEngine.Color.green or CS.UnityEngine.Color.red

    -- 绘制地面检测范围的线框球体
    self.gizmos:DrawWireSphere(checkPosition, self.playerData.groundCheckRadius, color)
end

return PlayerController