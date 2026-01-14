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

return StringUtil
