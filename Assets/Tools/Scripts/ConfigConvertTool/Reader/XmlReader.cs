using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SimpleXML;
using UnityEngine;

public class XmlReader : IConfigReader
{
    public ConfigFormat SupportedFormat => ConfigFormat.Xml;

    public ConfigData Read(string filePath)
    {
        var configData = new ConfigData();
        configData.PrimitiveFormat = ConfigFormat.Xml;

        try
        {
            // 读取XML内容
            string xmlContent = File.ReadAllText(filePath, Encoding.UTF8);
            XMLNode rootNode = SimpleXML.XML.Parse(xmlContent);

            // 优先判断键值对格式（包含对象数组）
            if (IsKeyValueFormat(rootNode))
            {
                configData.Mode = ConfigMode.KeyValue;
                return ReadKeyValue(rootNode, configData);
            }
            // 判断值数组格式（使用XElement辅助验证节点名称）
            else if (IsArrayFormat(rootNode))
            {
                configData.Mode = ConfigMode.Array;
                return ReadArray(rootNode, configData);
            }
            else
            {
                Debug.LogError($"不支持的XML格式: {filePath}");
                return configData;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"解析XML文件时出错: {filePath}\n{ex.Message}");
            return configData;
        }
    }

    #region 格式判断

    /// <summary>
    /// 验证是否为值数组格式（表格）：
    /// 1. rootNode是数组类型
    /// 2. 所有元素都是简单类型（非对象）
    /// 3. 所有子元素节点名称相同（通过XElement验证）
    /// </summary>
    private bool IsArrayFormat(XMLNode rootNode)
    {
        if (!rootNode.IsArray) return false;

        var array = rootNode.AsArray;
        // 检查数组元素是否均为简单类型（非对象）
        foreach (var element in array)
        {
            if (element.IsObject)
                return false; // 包含对象元素，不属于值数组
        }

        Debug.Log($"XML格式为值数组（表格格式）");
        return true;
    }

    /// <summary>
    /// 验证是否为键值对格式（对象数组或包含子对象的对象）
    /// </summary>
    private bool IsKeyValueFormat(XMLNode rootNode)
    {
        // 情况1：根节点是数组，且所有元素都是对象（对象数组）
        if (rootNode.IsArray)
        {
            var array = rootNode.AsArray;
            if (array.Count == 0) return false;
            
            foreach (var element in array)
            {
                if (!element.IsObject)
                    return false; // 存在非对象元素，不是对象数组
            }
            Debug.Log("XML格式为对象数组（键值对模式）");
            return true;
        }

        // 情况2：根节点是对象，且包含对象类型的子节点
        if (!rootNode.IsObject) return false;
        
        var obj = rootNode.AsObject;
        foreach (var key in obj.Keys)
        {
            if (!key.StartsWith("@") && obj[key].IsObject)
                return true; // 存在对象子节点
        }

        return false;
    }

    #endregion

    #region 解析逻辑

    /// <summary>
    /// 解析值表格格式（使用元素节点名称作为列名）
    /// 示例：<root><number>1</number><number>2</number></root> 会解析为列名"number"的数组
    /// </summary>
    private ConfigData ReadArray(XMLNode rootNode, ConfigData configData)
    {
        var array = rootNode.AsArray;
        var rows = new List<object[]>();

        // 获取元素节点名称作为列名（所有子元素名称相同，取第一个）
        string elementName = array.Count > 0 ? array[0].Name : "value";
        configData.Columns = new[] { elementName };

        // 解析数组元素值
        foreach (var element in array)
        {
            object[] row = new object[1];
            row[0] = ConvertXmlValueToObject(element);
            rows.Add(row);
        }

        configData.Rows = rows;
        return configData;
    }

    /// <summary>
    /// 解析键值对格式（支持对象数组和对象包含子对象两种形式）
    /// </summary>
    private ConfigData ReadKeyValue(XMLNode rootNode, ConfigData configData)
    {
        configData.RootNode = ConvertXmlToTreeNode(rootNode.Name, rootNode);
        return configData;
    }

    #endregion
    
    private TreeNode ConvertXmlToTreeNode(string nodeName, XMLNode xmlNode)
    { 
        if (xmlNode == null)
            return new TreeNode(nodeName, null, TreeNodeType.Value);

        // 处理对象类型（XMLObject）
        if (xmlNode.IsObject)
        {
            var treeNode = new TreeNode(nodeName, null, TreeNodeType.Object);
        
            // 添加XML属性（如<node @attr="value"/>中的@attr）
            foreach (var key in xmlNode.AsObject.Keys)
            {
                if (key.StartsWith("@")) // 属性节点以@开头（来自SimpleXML的约定）
                {
                    treeNode.AddAttribute(key.TrimStart('@'), xmlNode.AsObject[key].Value);
                }
            }

            // 处理子节点（非属性节点）
            foreach (var key in xmlNode.AsObject.Keys)
            {
                if (!key.StartsWith("@")) // 排除属性节点
                {
                    var childNode = ConvertXmlToTreeNode(key, xmlNode.AsObject[key]);
                    treeNode.AddChild(childNode);
                }
            }
            return treeNode;
        }
        // 处理数组类型（XMLArray）
        else if (xmlNode.IsArray)
        {
            var treeNode = new TreeNode(nodeName, null, TreeNodeType.Array);
            foreach (var child in xmlNode.AsArray.Children)
            {
                // 数组元素用索引作为节点名
                var index = xmlNode.AsArray.Children.ToList().IndexOf(child);
                var childNode = ConvertXmlToTreeNode(index.ToString(), child);
                treeNode.AddChild(childNode);
            }
            return treeNode;
        }
        // 处理值类型（字符串、数字、布尔等）
        else
        {
            object value = ConvertXmlValueToObject(xmlNode);
            return new TreeNode(nodeName, value, TreeNodeType.Value);
        }
    }
    
    /// <summary>
    /// 将XML值转换为合适的C#对象
    /// </summary>
    private object ConvertXmlValueToObject(XMLNode value)
    {
        if (value.IsNull)
            return null;
        if (value.IsBoolean)
            return value.AsBool;
        if (value.IsNumber)
            return value.AsDouble; // 统一用double处理数字，避免精度问题
        if (value.IsString)
            return value.Value;
        return value.Value; // 其他类型默认转为字符串
    }
}