--- DialogueModel 集成测试
--- 测试参数解析与 DialogueModel 的集成

local stringUtil = require("StringUtil")

print("========================================")
print("DialogueModel 集成测试")
print("========================================")

-- ==========================================
-- 测试 1: 模拟真实对话参数解析
-- ==========================================
print("\n=== 测试 1: 模拟真实对话参数解析 ===")

-- 模拟对话数据中的函数和参数字符串
local funcStr = ">TestFunc1;>TestFunc2;>TestFunc3"
local paramStr = 'param1;100&true&"hello";{x=10,y=20}&[1,2,3]'

-- 解析函数列表
local funcs = stringUtil.SplitSemicolon(funcStr)
print("解析函数列表:")
for i, func in ipairs(funcs) do
    print(string.format("  [%d] %s", i, func))
end

-- 解析参数组列表
local params = stringUtil.SplitSemicolon(paramStr)
print("\n解析参数组列表:")
for i, param in ipairs(params) do
    print(string.format("  [%d] %s", i, param))
end

-- 解析每个参数组
print("\n解析每个参数组的具体参数:")
for i, param in ipairs(params) do
    local paramList = stringUtil.SplitAmpersand(param)
    print(string.format("  参数组 [%d]:", i))
    for j, p in ipairs(paramList) do
        print(string.format("    [%d] %s (type: %s)", j, tostring(p), type(p)))
    end
end

-- ==========================================
-- 测试 2: 复杂嵌套参数
-- ==========================================
print("\n=== 测试 2: 复杂嵌套参数 ===")

local complexParam = '{player={name="hero",level=5,pos={x=100,y=200}},items=[1,2,3],active=true}'
local result = stringUtil.ParseValue(complexParam)

print("解析复杂嵌套参数:")
print(string.format("  player.name = %s", result.player.name))
print(string.format("  player.level = %d", result.player.level))
print(string.format("  player.pos.x = %d", result.player.pos.x))
print(string.format("  player.pos.y = %d", result.player.pos.y))
print(string.format("  items[1] = %d", result.items[1]))
print(string.format("  items[2] = %d", result.items[2]))
print(string.format("  items[3] = %d", result.items[3]))
print(string.format("  active = %s", tostring(result.active)))

-- ==========================================
-- 测试 3: 向后兼容性测试
-- ==========================================
print("\n=== 测试 3: 向后兼容性测试 ===")

-- 旧格式：简单字符串参数
local oldFormatParams = "param1&param2&param3"
local oldResult = stringUtil.SplitAmpersand(oldFormatParams)

print("旧格式参数解析 (向后兼容):")
for i, p in ipairs(oldResult) do
    print(string.format("  [%d] %s (type: %s)", i, p, type(p)))
end

-- 验证旧格式仍然返回字符串（裸字符串）
assert(type(oldResult[1]) == "string", "向后兼容：参数1应为字符串")
assert(type(oldResult[2]) == "string", "向后兼容：参数2应为字符串")
assert(type(oldResult[3]) == "string", "向后兼容：参数3应为字符串")
assert(oldResult[1] == "param1", "向后兼容：参数1值应为param1")
assert(oldResult[2] == "param2", "向后兼容：参数2值应为param2")
assert(oldResult[3] == "param3", "向后兼容：参数3值应为param3")

print("✓ 向后兼容性测试通过")

-- ==========================================
-- 测试 4: 混合新旧格式
-- ==========================================
print("\n=== 测试 4: 混合新旧格式 ===")

local mixedParams = 'param1&123&true&{x=10,y=20}'
local mixedResult = stringUtil.SplitAmpersand(mixedParams)

print("混合格式参数解析:")
for i, p in ipairs(mixedResult) do
    print(string.format("  [%d] %s (type: %s)", i, tostring(p), type(p)))
end

assert(type(mixedResult[1]) == "string", "混合格式：参数1应为字符串")
assert(type(mixedResult[2]) == "number", "混合格式：参数2应为数字")
assert(type(mixedResult[3]) == "boolean", "混合格式：参数3应为布尔值")
assert(type(mixedResult[4]) == "table", "混合格式：参数4应为表")

print("✓ 混合格式测试通过")

-- ==========================================
-- 测试 5: 模拟 DialogueModel 的使用场景
-- ==========================================
print("\n=== 测试 5: 模拟 DialogueModel 使用场景 ===")

-- 模拟 GetImmediateFunc 的逻辑
local function simulateGetImmediateFunc(funcStr, paramStr)
    local funcList = {}
    local paramList = {}
    
    local funcs = stringUtil.SplitSemicolon(funcStr)
    local params = stringUtil.SplitSemicolon(paramStr)
    
    for i, func in ipairs(funcs) do
        if string.sub(func, 1, 1) == ">" then
            table.insert(funcList, string.sub(func, 2))
            table.insert(paramList, stringUtil.SplitAmpersand(params[i] or ""))
        end
    end
    
    return funcList, paramList
end

-- 测试场景
local testFuncStr = ">ShowEffect;>PlaySound;>SetVariable"
local testParamStr = '"explosion"&"impact.wav";soundEffect&0.8;playerHealth&{current=100,max=100}'

local funcList, paramList = simulateGetImmediateFunc(testFuncStr, testParamStr)

print("模拟 GetImmediateFunc 结果:")
for i, funcName in ipairs(funcList) do
    print(string.format("  函数 [%d]: %s", i, funcName))
    local params = paramList[i]
    for j, param in ipairs(params) do
        print(string.format("    参数 [%d]: %s (type: %s)", j, tostring(param), type(param)))
    end
end

-- 验证解析结果
assert(funcList[1] == "ShowEffect", "函数1应为ShowEffect")
assert(funcList[2] == "PlaySound", "函数2应为PlaySound")
assert(funcList[3] == "SetVariable", "函数3应为SetVariable")

assert(paramList[1][1] == "explosion", "函数1参数1应为explosion")
assert(paramList[1][2] == "impact.wav", "函数1参数2应为impact.wav")

assert(paramList[2][1] == "soundEffect", "函数2参数1应为soundEffect")
assert(paramList[2][2] == 0.8, "函数2参数2应为0.8")

assert(paramList[3][1] == "playerHealth", "函数3参数1应为playerHealth")
assert(type(paramList[3][2]) == "table", "函数3参数2应为表")
assert(paramList[3][2].current == 100, "函数3参数2.current应为100")
assert(paramList[3][2].max == 100, "函数3参数2.max应为100")

print("✓ DialogueModel 使用场景测试通过")

-- ==========================================
-- 测试 6: 空值和边界情况
-- ==========================================
print("\n=== 测试 6: 空值和边界情况 ===")

local emptyFunc = stringUtil.SplitSemicolon("")
assert(#emptyFunc == 0, "空字符串应返回空表")

local emptyParam = stringUtil.SplitAmpersand("")
assert(#emptyParam == 0, "空参数应返回空表")

local nilParam = stringUtil.SplitAmpersand("nil&nil&nil")
assert(nilParam[1] == nil, "nil值应正确解析")
assert(nilParam[2] == nil, "nil值应正确解析")
assert(nilParam[3] == nil, "nil值应正确解析")

print("✓ 空值和边界情况测试通过")

print("\n========================================")
print("所有集成测试通过!")
print("========================================")
