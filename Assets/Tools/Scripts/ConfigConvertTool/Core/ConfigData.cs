using System.Collections.Generic;

/// <summary>
/// 配置文件中间态数据
/// </summary>
public class ConfigData
{
    public ConfigFormat PrimitiveFormat { get; set;}

    public ConfigMode Mode { get; set;}
    
    // 表格模式数据
    public string[] Columns { get; set; }
    public List<object[]> Rows { get; set; }
    
    // 键值对模式数据
    public TreeNode RootNode { get; set; }
}

/// <summary>
/// 树节点，用于表示键值对模式的层次结构
/// </summary>
public class TreeNode
{
    public string Name { get; set; }
    public object Value { get; set; }
    public TreeNodeType NodeType { get; set; }
    public List<TreeNode> Children { get; set; }
    public Dictionary<string, string> Attributes { get; set; }
    
    public TreeNode()
    {
        Children = new List<TreeNode>();
        Attributes = new Dictionary<string, string>();
    }
    
    public TreeNode(string name, object value, TreeNodeType nodeType = TreeNodeType.Value) : this()
    {
        Name = name;
        Value = value;
        NodeType = nodeType;
    }
    
    public void AddChild(TreeNode child)
    {
        Children.Add(child);
    }
    
    public void AddAttribute(string key, string value)
    {
        Attributes[key] = value;
    }
}

/// <summary>
/// 树节点类型
/// </summary>
public enum TreeNodeType
{
    Object,  // 对象节点
    Array,   // 数组节点
    Value    // 值节点
}