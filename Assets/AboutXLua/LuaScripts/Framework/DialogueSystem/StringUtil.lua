--- 辅助工具类
local StringUtil = {}

---@function ;分隔字符串解析（去除空格）
---@param str string
function StringUtil.SplitSemicolon(str)
    if not str or str == "" then return {} end

    local result = {}
    for param in string.gmatch(str, "([^;]+)") do
        table.insert(result, string.match(param, "^%s*(.-)%s*$"))
    end
    return result
end

---@function &分隔字符串解析（去除空格）
---@param str string
function StringUtil.SplitAmpersand(str)
    if not str or str == "" then return {} end

    local result = {}
    for param in string.gmatch(str, "([^&]+)") do
        table.insert(result, string.match(param, "^%s*(.-)%s*$"))
    end
    return result
end

--- 去除字符串首尾空格
local function trim(s)
    if not s then return s end
    return (s:gsub("^%s*(.-)%s*$", "%1"))
end

--- 常见字符串转义处理
local function unescapeString(s)
    s = s:gsub('\\n', '\n')
    s = s:gsub('\\t', '\t')
    s = s:gsub('\\r', '\r')
    s = s:gsub('\\"', '"')
    s = s:gsub("\\'", "'")
    s = s:gsub('\\\\', '\\')
    return s
end

-- 提前声明函数
local parseValue, parseTable

--- 解析Lua表
function parseTable(inner)
    local items = {}
    local buf = {}
    local depth = 0
    local inQuote = false
    local quoteChar

    local len = #inner
    for i = 1, len do
        local ch = inner:sub(i,i)
        local prev = inner:sub(i-1,i-1)

        if not inQuote and (ch == '"' or ch == "'") then
            inQuote = true; quoteChar = ch
            table.insert(buf, ch)
        elseif inQuote and ch == quoteChar and prev ~= '\\' then
            inQuote = false; quoteChar = nil
            table.insert(buf, ch)
        elseif not inQuote then
            if ch == '{' then
                depth = depth + 1
                table.insert(buf, ch)
            elseif ch == '}' then
                depth = depth - 1
                table.insert(buf, ch)
            elseif ch == ',' and depth == 0 then
                local part = table.concat(buf)
                table.insert(items, trim(part))
                buf = {}
            else
                table.insert(buf, ch)
            end
        else
            table.insert(buf, ch)
        end
    end

    if #buf > 0 then
        table.insert(items, trim(table.concat(buf)))
    end

    local result = {}
    local arrayIndex = 1

    for _, token in ipairs(items) do
        if token == nil or token == '' then
            -- 跳过空
        else
            -- 找到首个等号
            local eqPos
            local inQ = false
            local qch
            local d = 0
            for i = 1, #token do
                local c = token:sub(i,i)
                local prev = token:sub(i-1,i-1)
                if not inQ and (c == '"' or c == "'") then
                    inQ = true; qch = c
                elseif inQ and c == qch and prev ~= '\\' then
                    inQ = false; qch = nil
                elseif not inQ then
                    if c == '{' then d = d + 1
                    elseif c == '}' then d = d - 1
                    elseif c == '=' and d == 0 then
                        eqPos = i
                        break
                    end
                end
            end

            -- 键值对形式
            if eqPos then
                local keyStr = trim(token:sub(1, eqPos - 1))
                local valStr = trim(token:sub(eqPos + 1))

                -- 解析key
                local key
                if #keyStr >= 2 then
                    local f = keyStr:sub(1,1); local l = keyStr:sub(-1,-1)
                    if (f == '"' and l == '"') or (f == "'" and l == "'") then
                        key = unescapeString(keyStr:sub(2, -2))
                    end
                end
                if not key then
                    local num = tonumber(keyStr)
                    if num ~= nil then key = num else key = keyStr end
                end
                result[key] = parseValue(valStr)
            else
                -- 没有key，按顺序作为数组元素
                result[arrayIndex] = parseValue(token)
                arrayIndex = arrayIndex + 1
            end
        end
    end

    return result
end

--- 解析Lua字符串
function parseValue(s)
    if s == nil then return nil end
    s = trim(s)
    if s == "" then return "" end

    local lower = string.lower(s)
    if lower == "nil" then return nil end
    if lower == "true" then return true end
    if lower == "false" then return false end

    -- 字符串转义
    if #s >= 2 then
        local first = s:sub(1,1)
        local last = s:sub(-1,-1)
        if (first == '"' and last == '"') or (first == "'" and last == "'") then
            return unescapeString(s:sub(2, -2))
        end
    end

    -- 解析表类型
    if s:sub(1,1) == '{' and s:sub(-1,-1) == '}' then
        local inner = s:sub(2, -2)
        return parseTable(inner)
    end

    -- number
    local n = tonumber(s)
    if n ~= nil then return n end
    
    return s
end

---@function 将参数字符串列表推导解析
function StringUtil.ParseParamList(paramStrList)
    if not paramStrList then return {} end
    local out = {}
    for _, p in ipairs(paramStrList) do
        table.insert(out, parseValue(p))
    end
    return out
end

return StringUtil
