using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class CsvReader : IConfigReader
{
    public ConfigFormat SupportedFormat => ConfigFormat.Csv;
    
    public ConfigData Read(string filePath)
    {
        var configData = new ConfigData();
        configData.PrimitiveFormat = ConfigFormat.Csv;

        // 读取CSV文件所有行
        string[] allLines = File.ReadAllLines(filePath, Encoding.UTF8);
        
        try
        {
            if (IsArrayFormat(allLines))
            {
                configData.Mode = ConfigMode.Array;
                return ReadArray(allLines, configData);
            }
            else if (IsKeyValueFormat(allLines))
            {
                configData.Mode = ConfigMode.KeyValue;
                return ReadKeyValue(allLines, configData);
            }
            else
            {
                Debug.LogError($"不支持的Csv格式: {filePath}");
                return configData;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"解析Csv文件时出错: {filePath}\n{ex.Message}");
            return configData;
        }
    }

    #region  检查Csv格式

    public bool IsArrayFormat(string[] allLines)
    {
        // TODO: 检查Csv文件是否为数组格式
        return true;
    }
    
    public bool IsKeyValueFormat(string[] allLines)
    {
        // TODO: 检查Csv文件是否为键值对格式
        Debug.LogError("暂时不支持键值对格式");
        return false;
    }

    #endregion

    #region  解析Csv
    
    public ConfigData ReadArray(string[] allLines,ConfigData configData)
    {
        var rows = new List<object[]>();

        if (allLines.Length == 0)
            return configData;

        // 第一行为列名（Columns）
        configData.Columns = ParseCsvLine(allLines[0]);

        // 从第二行开始解析数据行（Rows）
        for (int i = 1; i < allLines.Length; i++)
        {
            if (string.IsNullOrEmpty(allLines[i]))
                continue;

            object[] rowValues = ParseCsvLine(allLines[i]);
            rows.Add(rowValues);
        }

        configData.Rows = rows;
        return configData;
    }
    
    public ConfigData ReadKeyValue(string[] allLines,ConfigData data)
    {
        // TODO: 解析CSV文件为键值对格式
        Debug.LogError("暂时不支持键值对格式");
        return data;
    }
    
    #endregion
    
    /// <summary>
    /// 解析CSV单行（处理逗号分隔和引号转义）
    /// </summary>
    private string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                // 处理双引号转义（""表示一个引号）
                if (inQuotes && i < line.Length - 1 && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // 跳过下一个引号
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString()); // 添加最后一个字段
        return values.ToArray();
    }
}