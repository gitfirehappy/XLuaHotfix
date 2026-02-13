--- 对话函数实现
--- 实现 IDialogueFuncProvider 接口以支持注册表扫描
local DemoDialogueFunctions = {}
local mt = {__index = require("DialogueFuncRegistry").IDialogueFuncProvider}
setmetatable(DemoDialogueFunctions, mt)

--- 规范定义：每个函数 = { name = "标识符", func = function(...) ... end }

--- 即时执行函数测试
DemoDialogueFunctions.TestImmediateFunc = {
    name = "TestImmediateFunc",
    func = function(param)
        CS.UnityEngine.Debug.Log("[Lua] 即时函数执行，参数: "..param)
    end
}

--- 交互执行函数测试
DemoDialogueFunctions.TestInteractiveFunc = {
    name = "TestInteractiveFunc",
    func = function(param)
        CS.UnityEngine.Debug.Log("[Lua] 交互函数执行，参数: "..param)
    end
} 

--- 条件判断函数测试
---@return string 选中的分支ID
DemoDialogueFunctions.CheckCondition = {
    name = "CheckCondition",
    paramTypes = {"string", "string"}, -- 指定参数类型为字符串
    func =  function(branchA, branchB)
        local condition = CS.UnityEngine.Random.Range(0, 2) == 0
        local result = condition and branchA or branchB
        result = branchB; -- 固定返回测试
        CS.UnityEngine.Debug.Log("[Lua] 条件判断，返回分支: "..result)
        return result
    end
}

--- 显示特效函数测试
DemoDialogueFunctions.ShowSpecialEffect = {
    name = "ShowSpecialEffect",
    func =  function(effectName)
        CS.UnityEngine.Debug.Log("[Lua] 显示特效: "..effectName)
    end
}

--- 播放音效函数测试
DemoDialogueFunctions.PlaySound = {
    name = "PlaySound",
    func =  function(soundName)
    CS.UnityEngine.Debug.Log("[Lua] 播放音效: "..soundName)
end
}

--- 启动新对话函数测试
DemoDialogueFunctions.StartDialogue = {
    name = "StartDialogue",
    func =  function(fileName)
        CS.UnityEngine.Debug.Log("[Lua] 启动新对话: "..fileName)
        local DialogueController = require("DialogueController")
        DialogueController.Start(fileName)
    end
}

---@function 辅助打印表
local stringUtil = require("StringUtil")

---@function 测试List参数
DemoDialogueFunctions.TestList = {
    name = "TestList",
    paramTypes = {"table"}, -- 指定参数类型为表（列表）
    func = function(list)
        CS.UnityEngine.Debug.Log("[Lua Demo] TestList: " .. stringUtil.Dump(list))
    end
}

---@function 测试Dict参数
DemoDialogueFunctions.TestDict = {
    name = "TestDict",
    func = function(dict)
        CS.UnityEngine.Debug.Log("[Lua Demo] TestDict: " .. stringUtil.Dump(dict))
    end
}

return DemoDialogueFunctions