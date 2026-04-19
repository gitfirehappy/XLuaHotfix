using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class JsonReader : IConfigReader
{
    public ConfigFormat SupportedFormat => ConfigFormat.Json;

    public ConfigData Read(string filePath)
    {
        var configData = new ConfigData();
        configData.PrimitiveFormat = ConfigFormat.Json;

        // 读取JSON文件内容
        string jsonContent = File.ReadAllText(filePath, Encoding.UTF8);

        try
        {
            // 尝试解析为数组格式
            if (jsonContent.Trim().StartsWith("["))
            {
                configData.Mode = ConfigMode.Array;
                return ReadArray(jsonContent, configData);
            }
            // 尝试解析为对象格式（键值对）
            else if (jsonContent.Trim().StartsWith("{"))
            {
                configData.Mode = ConfigMode.KeyValue;
                return ReadKeyValue(jsonContent, configData);
            }
            else
            {
                Debug.LogError($"不支持的JSON格式: {filePath}");
                return configData;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"解析JSON文件时出错: {filePath}\n{ex.Message}");
            return configData;
        }
    }

    #region 解析JSON

    /// <summary>
    /// 解析数组格式的JSON
    /// </summary>
    public ConfigData ReadArray(string jsonContent, ConfigData configData)
    {
        var rows = new List<object[]>();

        // 使用SimpleJSON解析JSON数组
        var jsonArray = SimpleJSON.JSON.Parse(jsonContent).AsArray;

        if (jsonArray == null || jsonArray.Count == 0)
        {
            Debug.LogWarning("JSON数组为空或格式不正确");
            return configData;
        }

        // 从第一个对象获取所有字段名
        var firstItem = jsonArray[0].AsObject;
        List<string> columns = new List<string>();

        // 获取所有键
        foreach (var key in firstItem.Keys)
        {
            columns.Add(key);
        }

        configData.Columns = columns.ToArray();

        // 处理每一行数据
        for (int i = 0; i < jsonArray.Count; i++)
        {
            var item = jsonArray[i].AsObject;
            object[] row = new object[columns.Count];

            for (int j = 0; j < columns.Count; j++)
            {
                string columnName = columns[j];
                if (item.HasKey(columnName))
                {
                    var value = item[columnName];
                    // 根据值的类型进行处理
                    if (value.IsString)
                        row[j] = value.Value;
                    else if (value.IsNumber)
                        row[j] = value.AsFloat;
                    else if (value.IsBoolean)
                        row[j] = value.AsBool;
                    else
                        row[j] = value.Value; // 默认转为字符串
                }
                else
                {
                    row[j] = null;
                    Debug.LogWarning($"字段 '{columnName}' 在第 {i + 1} 个对象中不存在");
                }
            }

            rows.Add(row);
        }

        configData.Rows = rows;
        return configData;
    }

    /// <summary>
    /// 解析对象格式的JSON（键值对）
    /// </summary>
    public ConfigData ReadKeyValue(string jsonContent, ConfigData configData)
    {
        var jsonObject = SimpleJSON.JSON.Parse(jsonContent);
        
        // 构建树结构
        configData.RootNode = ConvertJsonToTreeNode("", jsonObject);
        
        return configData;
    }

    #endregion
    
    private TreeNode ConvertJsonToTreeNode(string nodeName, SimpleJSON.JSONNode jsonNode)
    { 
        if (jsonNode == null)
            return new TreeNode(nodeName, null, TreeNodeType.Value);

        // 处理对象类型
        if (jsonNode.IsObject)
        {
            var treeNode = new TreeNode(nodeName, null, TreeNodeType.Object);
            foreach (var key in jsonNode.Keys)
            {
                var childNode = ConvertJsonToTreeNode(key, jsonNode[key]);
                treeNode.AddChild(childNode);
            }
            return treeNode;
        }
        // 处理数组类型
        else if (jsonNode.IsArray)
        {
            var treeNode = new TreeNode(nodeName, null, TreeNodeType.Array);
            for (int i = 0; i < jsonNode.Count; i++)
            {
                // 数组元素使用索引作为节点名称
                var childNode = ConvertJsonToTreeNode(i.ToString(), jsonNode[i]);
                treeNode.AddChild(childNode);
            }
            return treeNode;
        }
        // 处理值类型
        else
        {
            object value = null;
            if (jsonNode.IsNumber)
                value = jsonNode.AsDouble;
            else if (jsonNode.IsBoolean)
                value = jsonNode.AsBool;
            else if (jsonNode.IsNull)
                value = null;
            else
                value = jsonNode.Value; // 字符串类型

            return new TreeNode(nodeName, value, TreeNodeType.Value);
        }
    }
}