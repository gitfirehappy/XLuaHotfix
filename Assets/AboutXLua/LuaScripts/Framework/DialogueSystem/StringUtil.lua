--- 辅助工具类
local StringUtil = {}

---@function 去除字符串首尾空格
---@param str string
---@return string
local function Trim(str)
    if not str then return "" end
    return string.match(str, "^%s*(.-)%s*$") or ""
end

---@function 智能分割表元素（尊重嵌套结构）
---@param str string 要分割的字符串
---@param delimiter string 分隔符（逗号或&）
---@return table 分割后的字符串数组
local function SplitTableElements(str, delimiter)
    local result = {}
    local current = ""
    local depth = 0  -- 嵌套深度
    local inString = false
    local escapeNext = false
    
    for i = 1, #str do
        local char = str:sub(i, i)
        
        if escapeNext then
            current = current .. char
            escapeNext = false
        elseif char == "\\" then
            current = current .. char
            escapeNext = true
        elseif char == '"' and not escapeNext then
            inString = not inString
            current = current .. char
        elseif not inString then
            if char == "{" or char == "[" then
                depth = depth + 1
                current = current .. char
            elseif char == "}" or char == "]" then
                depth = depth - 1
                current = current .. char
            elseif char == delimiter and depth == 0 then
                -- 遇到分隔符且不在嵌套结构中
                table.insert(result, current)
                current = ""
            else
                current = current .. char
            end
        else
            current = current .. char
        end
    end
    
    -- 添加最后一个元素
    if current ~= "" then
        table.insert(result, current)
    end
    
    return result
end

---@function 递归解析表结构
---@param str string 表字符串（如 "{x=10, y=20}" 或 "[1,2,3]"）
---@return table|nil 解析后的表，失败返回nil
local function ParseTable(str)
    str = Trim(str)
    
    -- 检查是否为数组格式 [...]
    local isArray = str:sub(1, 1) == "[" and str:sub(-1, -1) == "]"
    -- 检查是否为表格式 {...}
    local isTable = str:sub(1, 1) == "{" and str:sub(-1, -1) == "}"
    
    if not isArray and not isTable then
        return nil
    end
    
    -- 去除外层括号
    local content = str:sub(2, -2)
    content = Trim(content)
    
    if content == "" then
        return {}  -- 空表
    end
    
    local result = {}
    local elements = SplitTableElements(content, ",")
    
    for _, element in ipairs(elements) do
        element = Trim(element)
        
        -- 检查是否为键值对（key=value）
        local hasEquals = false
        local depth = 0
        local inString = false
        local equalsPos = nil
        
        for i = 1, #element do
            local char = element:sub(i, i)
            if char == '"' then
                inString = not inString
            elseif not inString then
                if char == "{" or char == "[" then
                    depth = depth + 1
                elseif char == "}" or char == "]" then
                    depth = depth - 1
                elseif char == "=" and depth == 0 then
                    hasEquals = true
                    equalsPos = i
                    break
                end
            end
        end
        
        if hasEquals and equalsPos then
            -- 键值对
            local key = Trim(element:sub(1, equalsPos - 1))
            local value = Trim(element:sub(equalsPos + 1))
            result[key] = StringUtil.ParseValue(value)
        else
            -- 数组元素
            table.insert(result, StringUtil.ParseValue(element))
        end
    end
    
    return result
end

---@function 递归解析单个参数值（支持所有类型）
---@param str string 参数字符串
---@return any 解析后的值（可能是string, number, boolean, table, nil）
function StringUtil.ParseValue(str)
    str = Trim(str)
    
    if str == "" or str == "nil" then
        return nil
    end
    
    -- 布尔值
    if str == "true" then
        return true
    elseif str == "false" then
        return false
    end
    
    -- 表结构（递归解析）
    if (str:sub(1, 1) == "{" and str:sub(-1, -1) == "}") or
       (str:sub(1, 1) == "[" and str:sub(-1, -1) == "]") then
        local tbl = ParseTable(str)
        if tbl ~= nil then
            return tbl
        end
    end
    
    -- 带引号的字符串
    if str:sub(1, 1) == '"' and str:sub(-1, -1) == '"' then
        local content = str:sub(2, -2)
        -- 处理转义字符
        content = content:gsub("\\(.)", {
            ["n"] = "\n",
            ["t"] = "\t",
            ["r"] = "\r",
            ["\\"] = "\\",
            ['"'] = '"'
        })
        return content
    end
    
    -- 数字（整数或浮点数）
    local num = tonumber(str)
    if num then
        return num
    end
    
    -- 裸字符串（没有引号的字符串）
    return str
end

---@function ;分隔字符串解析（去除空格）
---@param str string
---@return table 字符串数组
function StringUtil.SplitSemicolon(str)
    if not str or str == "" then return {} end

    local result = {}
    for param in string.gmatch(str, "([^;]+)") do
        table.insert(result, Trim(param))
    end
    return result
end

---@function &分隔字符串解析并转换为原生类型
---@param str string
---@return table 解析后的值数组（可能包含string, number, boolean, table等类型）
function StringUtil.SplitAmpersand(str)
    if not str or str == "" then return {} end

    local result = {}
    local elements = SplitTableElements(str, "&")
    
    for _, element in ipairs(elements) do
        table.insert(result, StringUtil.ParseValue(element))
    end
    
    return result
end

return StringUtil
