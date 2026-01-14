--- 对话函数实现
--- 实现 IDialogueFuncProvider 接口以支持注册表扫描
local DemoDialogueFunctions = {}
local mt = {__index = require("DialogueFuncRegistry").IDialogueFuncProvider}
setmetatable(DemoDialogueFunctions, mt)

---@function 元数据初始化（集中管理函数name，保证幂等）
function DemoDialogueFunctions:init()
    -- 幂等校验：防止重复执行
    if self.__inited then return end

    -- 集中赋值所有函数的name字段
    self.TestImmediateFunc.name = "测试先执行函数"
    self.TestInteractiveFunc.name = "测试后执行函数"
    self.CheckCondition.name = "测试条件判断函数"
    self.ShowSpecialEffect.name = "测试显示特效函数"
    self.PlaySound.name = "测试播放音效函数"
    self.StartDialogue.name = "StartDialogue"

    CS.UnityEngine.Debug.Log("[Lua-DemoDialogueFunctions] 元数据初始化完成")
end

---@function 即时执行函数测试
---@param param string 入参
function DemoDialogueFunctions.TestImmediateFunc(param)
    CS.UnityEngine.Debug.Log("[Lua] 即时函数执行，参数: "..param)
end

---@function 交互执行函数测试
---@param param string 入参
function DemoDialogueFunctions.TestInteractiveFunc(param)
    CS.UnityEngine.Debug.Log("[Lua] 交互函数执行，参数: "..param)
end

---@function 条件判断函数测试
---@param branchA string 分支A ID
---@param branchB string 分支B ID
---@return string 选中的分支ID
function DemoDialogueFunctions.CheckCondition(branchA, branchB)
    -- 复用C#的随机分支逻辑
    local condition = CS.UnityEngine.Random.Range(0, 2) == 0
    local result = condition and branchA or branchB
    CS.UnityEngine.Debug.Log("[Lua] 条件判断，返回分支: "..result)
    return result
end

---@function 显示特效函数测试
---@param effectName string 特效名
function DemoDialogueFunctions.ShowSpecialEffect(effectName)
    CS.UnityEngine.Debug.Log("[Lua] 显示特效: "..effectName)
end

---@function 播放音效函数测试
---@param soundName string 音效名
function DemoDialogueFunctions.PlaySound(soundName)
    CS.UnityEngine.Debug.Log("[Lua] 播放音效: "..soundName)
end

---@function 启动新对话函数测试
---@param fileName string 对话文件名
function DemoDialogueFunctions.StartDialogue(fileName)
    CS.UnityEngine.Debug.Log("[Lua] 启动新对话: "..fileName)
    -- 直接调用Lua控制器，避免C#<->Lua循环
    local DialogueController = require("DialogueController")
    DialogueController.Start(fileName)
end

return DemoDialogueFunctions