--- StringUtil 测试文件
--- 测试所有参数解析功能

local StringUtil = require("StringUtil")

-- 测试结果统计
local totalTests = 0
local passedTests = 0
local failedTests = 0

-- 辅助函数：打印测试结果
local function assert_equal(actual, expected, testName)
    totalTests = totalTests + 1
    local success = false
    
    if type(actual) == type(expected) then
        if type(actual) == "table" then
            success = table_equals(actual, expected)
        else
            success = (actual == expected)
        end
    end
    
    if success then
        passedTests = passedTests + 1
        print(string.format("✓ PASS: %s", testName))
    else
        failedTests = failedTests + 1
        print(string.format("✗ FAIL: %s", testName))
        print(string.format("  Expected: %s (type: %s)", tostring(expected), type(expected)))
        print(string.format("  Actual:   %s (type: %s)", tostring(actual), type(actual)))
    end
end

-- 辅助函数：比较两个表是否相等
local function table_equals(t1, t2)
    if t1 == t2 then return true end
    if type(t1) ~= "table" or type(t2) ~= "table" then return false end
    
    -- 检查所有键值对
    for k, v in pairs(t1) do
        if type(v) == "table" then
            if not table_equals(v, t2[k]) then return false end
        else
            if v ~= t2[k] then return false end
        end
    end
    
    for k, v in pairs(t2) do
        if type(v) == "table" then
            if not table_equals(v, t1[k]) then return false end
        else
            if v ~= t1[k] then return false end
        end
    end
    
    return true
end

-- 辅助函数：打印表内容（用于调试）
local function print_table(tbl, indent)
    indent = indent or 0
    local prefix = string.rep("  ", indent)
    
    if type(tbl) ~= "table" then
        print(prefix .. tostring(tbl))
        return
    end
    
    print(prefix .. "{")
    for k, v in pairs(tbl) do
        if type(v) == "table" then
            print(prefix .. "  [" .. tostring(k) .. "] = ")
            print_table(v, indent + 2)
        else
            print(prefix .. "  [" .. tostring(k) .. "] = " .. tostring(v) .. " (" .. type(v) .. ")")
        end
    end
    print(prefix .. "}")
end

print("========================================")
print("StringUtil 参数解析测试")
print("========================================")

-- ==========================================
-- 测试 1: 基础类型解析
-- ==========================================
print("\n=== 测试 1: 基础类型解析 ===")

-- 字符串
local result = StringUtil.ParseValue('"hello"')
assert_equal(result, "hello", "带引号的字符串")

result = StringUtil.ParseValue('world')
assert_equal(result, "world", "裸字符串")

-- 数字
result = StringUtil.ParseValue('123')
assert_equal(result, 123, "整数")

result = StringUtil.ParseValue('3.14')
assert_equal(result, 3.14, "浮点数")

-- 布尔值
result = StringUtil.ParseValue('true')
assert_equal(result, true, "布尔值 true")

result = StringUtil.ParseValue('false')
assert_equal(result, false, "布尔值 false")

-- nil
result = StringUtil.ParseValue('nil')
assert_equal(result, nil, "nil 值")

result = StringUtil.ParseValue('')
assert_equal(result, nil, "空字符串")

-- ==========================================
-- 测试 2: 简单数组解析
-- ==========================================
print("\n=== 测试 2: 简单数组解析 ===")

result = StringUtil.ParseValue('[1,2,3]')
assert_equal(result[1], 1, "数组索引1")
assert_equal(result[2], 2, "数组索引2")
assert_equal(result[3], 3, "数组索引3")

result = StringUtil.ParseValue('["a","b","c"]')
assert_equal(result[1], "a", "字符串数组索引1")
assert_equal(result[2], "b", "字符串数组索引2")
assert_equal(result[3], "c", "字符串数组索引3")

result = StringUtil.ParseValue('[true,false,123]')
assert_equal(result[1], true, "混合数组索引1")
assert_equal(result[2], false, "混合数组索引2")
assert_equal(result[3], 123, "混合数组索引3")

-- ==========================================
-- 测试 3: 哈希表解析
-- ==========================================
print("\n=== 测试 3: 哈希表解析 ===")

result = StringUtil.ParseValue('{x=10, y=20}')
assert_equal(result.x, 10, "哈希表键x")
assert_equal(result.y, 20, "哈希表键y")

result = StringUtil.ParseValue('{name="hero", health=100}')
assert_equal(result.name, "hero", "哈希表字符串值")
assert_equal(result.health, 100, "哈希表数字值")

result = StringUtil.ParseValue('{active=true, level=5}')
assert_equal(result.active, true, "哈希表布尔值")
assert_equal(result.level, 5, "哈希表数字值")

-- ==========================================
-- 测试 4: 嵌套表解析
-- ==========================================
print("\n=== 测试 4: 嵌套表解析 ===")

result = StringUtil.ParseValue('{info={name="test",level=5}}')
assert_equal(result.info.name, "test", "嵌套表-内层name")
assert_equal(result.info.level, 5, "嵌套表-内层level")

result = StringUtil.ParseValue('{items=[1,2,3], count=3}')
assert_equal(result.items[1], 1, "嵌套数组-索引1")
assert_equal(result.items[2], 2, "嵌套数组-索引2")
assert_equal(result.items[3], 3, "嵌套数组-索引3")
assert_equal(result.count, 3, "嵌套表-count键")

result = StringUtil.ParseValue('[{x=1},{x=2},{x=3}]')
assert_equal(result[1].x, 1, "数组嵌套表-索引1")
assert_equal(result[2].x, 2, "数组嵌套表-索引2")
assert_equal(result[3].x, 3, "数组嵌套表-索引3")

-- ==========================================
-- 测试 5: 广义表（混合）
-- ==========================================
print("\n=== 测试 5: 广义表解析 ===")

result = StringUtil.ParseValue('{1,2,x=3}')
assert_equal(result[1], 1, "广义表-数组元素1")
assert_equal(result[2], 2, "广义表-数组元素2")
assert_equal(result.x, 3, "广义表-键值对")

result = StringUtil.ParseValue('{1,"two",three="3"}')
assert_equal(result[1], 1, "广义表-数字")
assert_equal(result[2], "two", "广义表-字符串")
assert_equal(result.three, "3", "广义表-键值对字符串")

-- ==========================================
-- 测试 6: SplitAmpersand 混合参数
-- ==========================================
print("\n=== 测试 6: SplitAmpersand 混合参数解析 ===")

result = StringUtil.SplitAmpersand('123&"hello"&{x=10,y=20}&[1,2,3]')
assert_equal(result[1], 123, "SplitAmpersand-参数1(数字)")
assert_equal(result[2], "hello", "SplitAmpersand-参数2(字符串)")
assert_equal(result[3].x, 10, "SplitAmpersand-参数3(表).x")
assert_equal(result[3].y, 20, "SplitAmpersand-参数3(表).y")
assert_equal(result[4][1], 1, "SplitAmpersand-参数4(数组)[1]")
assert_equal(result[4][2], 2, "SplitAmpersand-参数4(数组)[2]")
assert_equal(result[4][3], 3, "SplitAmpersand-参数4(数组)[3]")

result = StringUtil.SplitAmpersand('{info={name="test",level=5}}&true&100')
assert_equal(result[1].info.name, "test", "嵌套表参数-name")
assert_equal(result[1].info.level, 5, "嵌套表参数-level")
assert_equal(result[2], true, "布尔参数")
assert_equal(result[3], 100, "数字参数")

-- ==========================================
-- 测试 7: SplitSemicolon 向后兼容
-- ==========================================
print("\n=== 测试 7: SplitSemicolon 向后兼容 ===")

result = StringUtil.SplitSemicolon('func1;func2;func3')
assert_equal(result[1], "func1", "SplitSemicolon-函数1")
assert_equal(result[2], "func2", "SplitSemicolon-函数2")
assert_equal(result[3], "func3", "SplitSemicolon-函数3")

result = StringUtil.SplitSemicolon('param1;param2&param3;param4')
assert_equal(result[1], "param1", "SplitSemicolon-参数组1")
assert_equal(result[2], "param2&param3", "SplitSemicolon-参数组2")
assert_equal(result[3], "param4", "SplitSemicolon-参数组3")

-- ==========================================
-- 测试 8: 边界条件
-- ==========================================
print("\n=== 测试 8: 边界条件 ===")

result = StringUtil.ParseValue('{}')
assert_equal(type(result), "table", "空表类型")

result = StringUtil.ParseValue('[]')
assert_equal(type(result), "table", "空数组类型")

result = StringUtil.SplitAmpersand('')
assert_equal(#result, 0, "空字符串分割")

result = StringUtil.SplitSemicolon('')
assert_equal(#result, 0, "空字符串分号分割")

-- 转义字符
result = StringUtil.ParseValue('"hello\\nworld"')
assert_equal(result, "hello\nworld", "转义换行符")

result = StringUtil.ParseValue('"test\\"quote"')
assert_equal(result, 'test"quote', "转义引号")

result = StringUtil.ParseValue('"path\\\\to\\\\file"')
assert_equal(result, 'path\\to\\file', "转义反斜杠")

-- 测试包含转义引号的字符串在表中
result = StringUtil.ParseValue('{msg="He said \\"Hello\\""}')
assert_equal(result.msg, 'He said "Hello"', "表中的转义引号")

-- ==========================================
-- 测试 9: 复杂嵌套场景
-- ==========================================
print("\n=== 测试 9: 复杂嵌套场景 ===")

result = StringUtil.ParseValue('{player={name="hero",pos={x=10,y=20}},items=[1,2,3]}')
assert_equal(result.player.name, "hero", "三层嵌套-player.name")
assert_equal(result.player.pos.x, 10, "三层嵌套-player.pos.x")
assert_equal(result.player.pos.y, 20, "三层嵌套-player.pos.y")
assert_equal(result.items[1], 1, "三层嵌套-items[1]")
assert_equal(result.items[2], 2, "三层嵌套-items[2]")
assert_equal(result.items[3], 3, "三层嵌套-items[3]")

-- ==========================================
-- 测试 10: 实际应用场景（模拟DialogueModel使用）
-- ==========================================
print("\n=== 测试 10: 实际应用场景 ===")

-- 模拟对话函数参数
local funcStr = ">TestFunc1;>TestFunc2"
local paramStr = 'param1&100&true;{x=10,y=20}&["a","b"]'

local funcs = StringUtil.SplitSemicolon(funcStr)
local params = StringUtil.SplitSemicolon(paramStr)

assert_equal(funcs[1], ">TestFunc1", "函数1")
assert_equal(funcs[2], ">TestFunc2", "函数2")

local params1 = StringUtil.SplitAmpersand(params[1])
assert_equal(params1[1], "param1", "函数1-参数1")
assert_equal(params1[2], 100, "函数1-参数2")
assert_equal(params1[3], true, "函数1-参数3")

local params2 = StringUtil.SplitAmpersand(params[2])
assert_equal(params2[1].x, 10, "函数2-参数1.x")
assert_equal(params2[1].y, 20, "函数2-参数1.y")
assert_equal(params2[2][1], "a", "函数2-参数2[1]")
assert_equal(params2[2][2], "b", "函数2-参数2[2]")

-- ==========================================
-- 测试结果汇总
-- ==========================================
print("\n========================================")
print("测试结果汇总")
print("========================================")
print(string.format("总测试数: %d", totalTests))
print(string.format("通过: %d", passedTests))
print(string.format("失败: %d", failedTests))
print(string.format("成功率: %.2f%%", (passedTests / totalTests) * 100))
print("========================================")

return {
    totalTests = totalTests,
    passedTests = passedTests,
    failedTests = failedTests
}
