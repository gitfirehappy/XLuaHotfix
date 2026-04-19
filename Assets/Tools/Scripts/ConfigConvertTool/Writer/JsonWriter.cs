using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class JsonWriter : IConfigWriter
{
    public ConfigFormat SupportedFormat => ConfigFormat.Json;

    public void Write(string outputPath, ConfigData data, WriterOptions options = null)
    {
        options = options ?? new WriterOptions();

        using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var writer = new StreamWriter(stream, options.Encoding))
        {
            // Json不允许注释
            
            switch (data.Mode)
            {
                case ConfigMode.Array:
                    WriteArray(writer, data, options);
                    break;
                case ConfigMode.KeyValue:
                    WriteKeyValue(writer, data, options);
                    break;
                default:
                    Debug.LogError($"不支持的配置模式: {data.Mode}");
                    break;
            }
        }
    }

    private void WriteArray(StreamWriter writer, ConfigData data, WriterOptions options)
    {
        writer.WriteLine("[");

        for (int i = 0; i < data.Rows.Count; i++)
        {
            var row = data.Rows[i];
            writer.Write($"{options.Indent}{{");

            for (int j = 0; j < data.Columns.Length; j++)
            {
                var fieldName = data.Columns[j];
                var fieldValue = row[j];

                writer.Write($"\"{fieldName}\": {FormatJsonValue(fieldValue)}");
                if (j < data.Columns.Length - 1)
                {
                    writer.Write(", ");
                }
            }

            writer.Write("}");
            if (i < data.Rows.Count - 1)
            {
                writer.Write(",");
            }

            writer.WriteLine();
        }

        writer.WriteLine("]");
    }

    private void WriteKeyValue(StreamWriter writer, ConfigData data, WriterOptions options)
    {
        if (data.RootNode != null)
        {
            // 与LuaWriter保持一致，根节点作为完整JSON对象的属性
            writer.WriteLine("{");
            WriteTreeNode(writer, data.RootNode, options.Indent, 1, false);
            writer.WriteLine();
            writer.WriteLine("}");
        }
        else
        {
            writer.WriteLine("{}");
        }
    }

    private void WriteTreeNode(StreamWriter writer, TreeNode node, string indent, int depth, bool isArrayElement)
    {
        var currentIndent = GetIndent(indent, depth);
        var childIndent = GetIndent(indent, depth + 1);

        // 如果不是数组元素，写入节点名称
        if (!isArrayElement)
        {
            writer.Write($"{currentIndent}\"{node.Name}\": ");
        }

        switch (node.NodeType)
        {
            case TreeNodeType.Object:
                writer.WriteLine("{");
                
                // 写入属性（如果有）
                if (node.Attributes.Count > 0)
                {
                    for (int i = 0; i < node.Attributes.Count; i++)
                    {
                        var attr = node.Attributes.ElementAt(i);
                        writer.Write($"{childIndent}\"@{attr.Key}\": {FormatJsonValue(attr.Value)}");
                        
                        // 如果有子节点或还有更多属性，添加逗号
                        if (node.Children.Count > 0 || i < node.Attributes.Count - 1)
                            writer.WriteLine(",");
                        else
                            writer.WriteLine();
                    }
                }

                // 写入子节点
                for (int i = 0; i < node.Children.Count; i++)
                {
                    var child = node.Children[i];
                    WriteTreeNode(writer, child, indent, depth + 1, false);
                    
                    if (i < node.Children.Count - 1)
                        writer.WriteLine(",");
                    else
                        writer.WriteLine();
                }

                writer.Write($"{currentIndent}}}");
                break;

            case TreeNodeType.Array:
                writer.WriteLine("[");
                for (int i = 0; i < node.Children.Count; i++)
                {
                    var child = node.Children[i];
                    writer.Write(childIndent);
                    WriteTreeNodeValue(writer, child, indent, depth + 1);
                    
                    if (i < node.Children.Count - 1)
                        writer.WriteLine(",");
                    else
                        writer.WriteLine();
                }
                writer.Write($"{currentIndent}]");
                break;

            case TreeNodeType.Value:
                writer.Write(FormatJsonValue(node.Value));
                break;
        }
    }

    /// <summary>
    /// 写入节点值（用于数组元素）
    /// </summary>
    private void WriteTreeNodeValue(StreamWriter writer, TreeNode node, string indent, int depth)
    {
        switch (node.NodeType)
        {
            case TreeNodeType.Object:
            case TreeNodeType.Array:
                WriteTreeNode(writer, node, indent, depth, true);
                break;
            default:
                writer.Write(FormatJsonValue(node.Value));
                break;
        }
    }

    /// <summary>
    /// 获取指定深度的缩进字符串
    /// </summary>
    private string GetIndent(string baseIndent, int depth)
    {
        return string.Concat(System.Linq.Enumerable.Repeat(baseIndent, depth));
    }

    /// <summary>
    /// 格式化JSON值（处理不同类型和转义）
    /// </summary>
    private string FormatJsonValue(object value)
    {
        if (value == null)
            return "null";

        // 处理字符串类型
        if (value is string str)
        {
            // 转义特殊字符
            str = str.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("/", "\\/")
                .Replace("\b", "\\b")
                .Replace("\f", "\\f")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            return $"\"{str}\"";
        }

        // 处理布尔类型
        if (value is bool boolVal)
        {
            return boolVal ? "true" : "false";
        }

        // 处理数字类型（直接返回）
        if (value is int || value is float || value is double || value is decimal)
        {
            return value.ToString();
        }

        // 默认转为字符串
        return $"\"{value}\"";
    }
}