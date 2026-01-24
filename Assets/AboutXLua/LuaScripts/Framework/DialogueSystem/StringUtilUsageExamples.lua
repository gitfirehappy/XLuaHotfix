--- 使用示例：展示新的参数解析功能
--- 演示如何在对话系统中使用增强的参数解析

local stringUtil = require("StringUtil")

print("========================================")
print("StringUtil 增强参数解析 - 使用示例")
print("========================================")

-- ==========================================
-- 示例 1: 基础参数类型
-- ==========================================
print("\n=== 示例 1: 基础参数类型 ===")
print("在对话配置中使用不同的参数类型：")

local dialogue1 = {
    Func = ">ShowMessage",
    Params = '"欢迎来到游戏世界！"'
}

local params1 = stringUtil.SplitAmpersand(dialogue1.Params)
print(string.format("  ShowMessage 接收参数: %s (类型: %s)", params1[1], type(params1[1])))

local dialogue2 = {
    Func = ">AddGold",
    Params = '100'
}

local params2 = stringUtil.SplitAmpersand(dialogue2.Params)
print(string.format("  AddGold 接收参数: %s (类型: %s)", params2[1], type(params2[1])))

local dialogue3 = {
    Func = ">SetFlag",
    Params = 'questCompleted&true'
}

local params3 = stringUtil.SplitAmpersand(dialogue3.Params)
print(string.format("  SetFlag 接收参数: %s, %s (类型: %s, %s)", 
    params3[1], tostring(params3[2]), type(params3[1]), type(params3[2])))

-- ==========================================
-- 示例 2: 数组参数
-- ==========================================
print("\n=== 示例 2: 数组参数 ===")
print("传递物品列表或技能列表：")

local dialogue4 = {
    Func = ">AddItems",
    Params = '[1,2,3,4,5]'  -- 物品ID数组
}

local params4 = stringUtil.SplitAmpersand(dialogue4.Params)
print("  AddItems 接收物品ID数组:")
for i, itemId in ipairs(params4[1]) do
    print(string.format("    物品 %d: ID=%d", i, itemId))
end

local dialogue5 = {
    Func = ">UnlockSkills",
    Params = '["火球术","冰冻术","治疗术"]'  -- 技能名称数组
}

local params5 = stringUtil.SplitAmpersand(dialogue5.Params)
print("  UnlockSkills 接收技能名称数组:")
for i, skillName in ipairs(params5[1]) do
    print(string.format("    技能 %d: %s", i, skillName))
end

-- ==========================================
-- 示例 3: 哈希表参数
-- ==========================================
print("\n=== 示例 3: 哈希表参数 ===")
print("传递位置信息或配置数据：")

local dialogue6 = {
    Func = ">TeleportPlayer",
    Params = '{x=100, y=200, z=0}'
}

local params6 = stringUtil.SplitAmpersand(dialogue6.Params)
print("  TeleportPlayer 接收位置信息:")
print(string.format("    坐标: (x=%d, y=%d, z=%d)", 
    params6[1].x, params6[1].y, params6[1].z))

local dialogue7 = {
    Func = ">ConfigureEnemy",
    Params = '{name="Boss",health=1000,attack=50,defense=30}'
}

local params7 = stringUtil.SplitAmpersand(dialogue7.Params)
print("  ConfigureEnemy 接收敌人配置:")
print(string.format("    名称: %s", params7[1].name))
print(string.format("    生命值: %d", params7[1].health))
print(string.format("    攻击力: %d", params7[1].attack))
print(string.format("    防御力: %d", params7[1].defense))

-- ==========================================
-- 示例 4: 嵌套表参数
-- ==========================================
print("\n=== 示例 4: 嵌套表参数 ===")
print("传递复杂的游戏数据：")

local dialogue8 = {
    Func = ">UpdatePlayer",
    Params = '{name="英雄",level=10,stats={hp=500,mp=200},position={x=100,y=200}}'
}

local params8 = stringUtil.SplitAmpersand(dialogue8.Params)
local player = params8[1]
print("  UpdatePlayer 接收玩家数据:")
print(string.format("    姓名: %s", player.name))
print(string.format("    等级: %d", player.level))
print(string.format("    生命值: %d", player.stats.hp))
print(string.format("    魔法值: %d", player.stats.mp))
print(string.format("    位置: (x=%d, y=%d)", player.position.x, player.position.y))

-- ==========================================
-- 示例 5: 多个混合参数
-- ==========================================
print("\n=== 示例 5: 多个混合参数 ===")
print("一个函数接收多个不同类型的参数：")

local dialogue9 = {
    Func = ">StartBattle",
    Params = '"森林入口"&{x=150,y=250}&["哥布林","史莱姆","骷髅"]&true&3'
}

local params9 = stringUtil.SplitAmpersand(dialogue9.Params)
print("  StartBattle 接收参数:")
print(string.format("    [1] 场景名称: %s (类型: %s)", params9[1], type(params9[1])))
print(string.format("    [2] 战斗位置: x=%d, y=%d (类型: %s)", 
    params9[2].x, params9[2].y, type(params9[2])))
print("    [3] 敌人列表: (类型: table)")
for i, enemy in ipairs(params9[3]) do
    print(string.format("        - %s", enemy))
end
print(string.format("    [4] 可逃跑: %s (类型: %s)", tostring(params9[4]), type(params9[4])))
print(string.format("    [5] 难度等级: %d (类型: %s)", params9[5], type(params9[5])))

-- ==========================================
-- 示例 6: 多函数多参数
-- ==========================================
print("\n=== 示例 6: 多函数多参数 ===")
print("一个对话节点执行多个函数：")

local dialogue10 = {
    Func = ">PlaySound;>ShowEffect;>AddReward",
    Params = '"victory.wav"&0.8;"explosion"&{x=100,y=200};{gold=500,exp=1000,items=[10,11,12]}'
}

local funcs = stringUtil.SplitSemicolon(dialogue10.Func)
local paramGroups = stringUtil.SplitSemicolon(dialogue10.Params)

print("  执行的函数序列:")
for i, func in ipairs(funcs) do
    local funcName = string.sub(func, 2)  -- 去除>前缀
    local params = stringUtil.SplitAmpersand(paramGroups[i] or "")
    
    print(string.format("  %d. %s", i, funcName))
    
    if funcName == "PlaySound" then
        print(string.format("     - 音效文件: %s", params[1]))
        print(string.format("     - 音量: %.1f", params[2]))
    elseif funcName == "ShowEffect" then
        print(string.format("     - 特效名称: %s", params[1]))
        print(string.format("     - 位置: (x=%d, y=%d)", params[2].x, params[2].y))
    elseif funcName == "AddReward" then
        print(string.format("     - 金币: %d", params[1].gold))
        print(string.format("     - 经验值: %d", params[1].exp))
        print("     - 物品列表:")
        for j, itemId in ipairs(params[1].items) do
            print(string.format("       * 物品ID: %d", itemId))
        end
    end
end

-- ==========================================
-- 示例 7: 向后兼容
-- ==========================================
print("\n=== 示例 7: 向后兼容 ===")
print("旧格式的字符串参数仍然正常工作：")

local oldDialogue = {
    Func = ">OldFunction",
    Params = "param1&param2&param3"
}

local oldParams = stringUtil.SplitAmpersand(oldDialogue.Params)
print("  OldFunction 接收参数 (兼容旧格式):")
for i, param in ipairs(oldParams) do
    print(string.format("    [%d] %s (类型: %s)", i, param, type(param)))
end

print("\n========================================")
print("所有示例展示完成！")
print("========================================")
print("\n提示：")
print("- 字符串可以使用引号或不使用引号")
print("- 数字会自动识别为 number 类型")
print("- 布尔值 true/false 会自动识别")
print("- 表可以是数组 [...] 或哈希表 {...}")
print("- 支持任意深度的嵌套")
print("- 完全向后兼容旧格式")
