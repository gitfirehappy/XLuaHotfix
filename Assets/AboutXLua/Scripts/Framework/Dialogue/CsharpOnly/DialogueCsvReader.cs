using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 将CSV文本转换为DialogueData列表
/// </summary>
public static class DialogueCsvReader
{
    /// <summary>
    /// 解析CSV文本为对话数据
    /// CSV列顺序：Sign,ID,Character,PosAndOp,Content,NextID,Func,Params
    /// </summary>
    public static List<DialogueData> ParseCsv(TextAsset csvAsset)
    {
        var result = new List<DialogueData>();
        if (csvAsset == null)
        {
            Debug.LogError("[DialogueCsvReader] CSV文本为空");
            return result;
        }

        // 按行分割（处理换行符）
        var lines = csvAsset.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) // 无数据行（仅表头）
        {
            Debug.LogWarning("[DialogueCsvReader] CSV无有效数据行");
            return result;
        }

        // 跳过表头，解析数据行
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // 分割列（兼容CSV标准逗号分隔，忽略引号内的逗号）
            var columns = ParseCsvLine(line);
            if (columns.Count < 8)
            {
                Debug.LogWarning($"[DialogueCsvReader] 第{i}行列数不足，跳过：{line}");
                continue;
            }

            var dialogue = new DialogueData
            {
                Sign = columns[0].Trim(),
                ID = columns[1].Trim(),
                Character = columns[2].Trim(),
                PosAndOp = columns[3].Trim(),
                Content = columns[4].Trim(),
                NextID = columns[5].Trim(),
                Func = columns[6].Trim(),
                Params = columns[7].Trim()
            };
            result.Add(dialogue);
        }

        return result;
    }

    /// <summary>
    /// 解析单行CSV（处理引号包裹的内容）
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var columns = new List<string>();
        var inQuotes = false;
        var currentColumn = "";

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                columns.Add(currentColumn);
                currentColumn = "";
            }
            else
            {
                currentColumn += c;
            }
        }
        columns.Add(currentColumn); // 最后一列
        return columns;
    }
}