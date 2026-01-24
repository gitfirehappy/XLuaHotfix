# StringUtil 参数解析功能文档

## 概述

StringUtil.lua 提供了强大的参数解析功能，支持递归解析复杂的表结构、自动类型推导和向后兼容。

## 功能特性

### 1. 支持的数据类型

- **字符串**: `"hello"` (带引号) 或 `hello` (裸字符串)
- **数字**: `123` (整数) 或 `3.14` (浮点数)
- **布尔值**: `true` 或 `false`
- **空值**: `nil` 或空字符串
- **表**: 支持数组、哈希表、嵌套表和广义表

### 2. 表结构支持

#### 数组 (Array)
```lua
-- 语法: [元素1, 元素2, ...]
local arr = StringUtil.ParseValue('[1,2,3]')
-- 结果: {1, 2, 3}

local strArr = StringUtil.ParseValue('["a","b","c"]')
-- 结果: {"a", "b", "c"}
```

#### 哈希表 (Hash Table)
```lua
-- 语法: {key1=value1, key2=value2, ...}
local hash = StringUtil.ParseValue('{x=10, y=20}')
-- 结果: {x=10, y=20}

local mixed = StringUtil.ParseValue('{name="hero", health=100}')
-- 结果: {name="hero", health=100}
```

#### 嵌套表 (Nested Table)
```lua
-- 支持任意深度的嵌套
local nested = StringUtil.ParseValue('{info={name="test",level=5}, items=[1,2,3]}')
-- 结果: {info={name="test", level=5}, items={1,2,3}}

local deep = StringUtil.ParseValue('{player={pos={x=10,y=20}}}')
-- 结果: {player={pos={x=10, y=20}}}
```

#### 广义表 (Generalized Table)
```lua
-- 同时包含数组元素和键值对
local general = StringUtil.ParseValue('{1,2,x=3,y=4}')
-- 结果: {1, 2, x=3, y=4}
```

## API 参考

### ParseValue(str)

递归解析单个参数值，自动识别类型。

**参数:**
- `str` (string): 要解析的参数字符串

**返回值:**
- (any): 解析后的值，可能是 string, number, boolean, table 或 nil

**示例:**
```lua
local num = StringUtil.ParseValue('123')          -- 123 (number)
local str = StringUtil.ParseValue('"hello"')      -- "hello" (string)
local bool = StringUtil.ParseValue('true')        -- true (boolean)
local tbl = StringUtil.ParseValue('{x=10,y=20}')  -- {x=10, y=20} (table)
local nothing = StringUtil.ParseValue('nil')      -- nil
```

### SplitAmpersand(str)

用 `&` 分隔符分割字符串，并将每个元素解析为原生 Lua 类型。

**参数:**
- `str` (string): 要分割的字符串

**返回值:**
- (table): 解析后的值数组

**示例:**
```lua
local params = StringUtil.SplitAmpersand('123&"hello"&true&{x=10}')
-- 结果: {123, "hello", true, {x=10}}

local mixed = StringUtil.SplitAmpersand('param1&100&[1,2,3]')
-- 结果: {"param1", 100, {1,2,3}}
```

**智能分割:**
- 自动识别嵌套的表结构，不会在表内部的逗号或 `&` 处分割
- 支持字符串中的转义字符

### SplitSemicolon(str)

用 `;` 分隔符分割字符串，返回字符串数组（保持向后兼容）。

**参数:**
- `str` (string): 要分割的字符串

**返回值:**
- (table): 字符串数组

**示例:**
```lua
local funcs = StringUtil.SplitSemicolon('func1;func2;func3')
-- 结果: {"func1", "func2", "func3"}
```

## 在对话系统中的使用

### 基本示例

在对话配置中，可以使用新的参数语法：

```lua
{
    Sign = "#",
    ID = "1",
    Func = ">ShowEffect;>PlaySound",
    Params = '"explosion"&{x=100,y=200}&0.5;"impact.wav"&0.8'
}
```

函数接收的参数将会是：
- `ShowEffect`: `"explosion"`, `{x=100, y=200}`, `0.5`
- `PlaySound`: `"impact.wav"`, `0.8`

### 复杂参数示例

```lua
{
    Sign = "#",
    ID = "2",
    Func = ">UpdatePlayer",
    Params = '{player={name="hero",level=5,pos={x=100,y=200}},items=[1,2,3]}'
}
```

函数 `UpdatePlayer` 将接收一个完整的玩家数据表作为参数。

### 向后兼容

旧的字符串参数格式仍然完全支持：

```lua
{
    Sign = "#",
    ID = "3",
    Func = ">OldFunction",
    Params = "param1&param2&param3"  -- 仍然有效
}
```

函数 `OldFunction` 将接收到：`"param1"`, `"param2"`, `"param3"`

## 类型推导规则

解析器按以下顺序进行类型推导：

1. **空值**: `""` 或 `"nil"` → `nil`
2. **布尔值**: `"true"` → `true`, `"false"` → `false`
3. **表**: `{...}` 或 `[...]` → 递归解析为 `table`
4. **带引号字符串**: `"..."` → 去除引号后的 `string`
5. **数字**: 可转换为数字 → `number`
6. **裸字符串**: 其他所有情况 → `string`

## 转义字符支持

在带引号的字符串中支持以下转义字符：

- `\n` → 换行符
- `\t` → 制表符
- `\r` → 回车符
- `\\` → 反斜杠
- `\"` → 双引号

**示例:**
```lua
local str = StringUtil.ParseValue('"Hello\\nWorld"')
-- 结果: "Hello\nWorld"
```

## 错误处理

- 如果表解析失败，返回 `nil` 并尝试作为其他类型解析
- 空字符串或 `"nil"` 返回 `nil`
- 格式错误的输入将被当作裸字符串处理

## 性能考虑

- 解析器使用递归算法，对于深度嵌套的表可能影响性能
- 建议在对话配置时避免过深的嵌套（推荐不超过 5 层）
- 字符串分割使用智能算法，会遍历整个字符串以识别嵌套结构

## 限制和注意事项

1. **不支持 Lua 代码执行**: 解析器只做字符串解析，不执行任何 Lua 代码
2. **不支持函数类型**: 参数中不能包含函数定义
3. **键必须是字符串**: 表的键只支持字符串，不支持数字作为显式键名
4. **空格敏感**: 在某些情况下，额外的空格可能被保留在裸字符串中

## 测试

运行测试套件验证功能：

```bash
lua5.3 StringUtilTest.lua
lua5.3 DialogueModelIntegrationTest.lua
```

所有测试应该 100% 通过。

## 版本历史

### v2.0 (当前版本)
- 添加递归表解析支持
- 添加类型推导功能
- 增强 `SplitAmpersand` 返回原生类型
- 添加 `ParseValue` 和 `ParseTable` 函数
- 保持向后兼容

### v1.0 (原始版本)
- 基本的字符串分割功能
- `SplitSemicolon` 和 `SplitAmpersand` 只返回字符串
