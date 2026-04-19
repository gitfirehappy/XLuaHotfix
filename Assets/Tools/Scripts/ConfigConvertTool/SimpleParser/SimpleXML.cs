using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Xml.Linq;


namespace SimpleXML
{
    /// <summary>
    /// XMLNodeTypeEX
    /// Examples:
    /// Array - <Items><Item>1</Item><Item>2</Item></Items>
    /// Object - <Player><Name>Hero</Name><Level>5</Level></Player>
    /// String - <Name>Hero</Name>
    /// Number - <Level>5</Level>
    /// NullValue - <Value></Value>
    /// Boolean - <IsActive>true</IsActive>
    /// None - <Something />
    /// </summary>
    public enum XMLNodeTypeEX
    {
        Array = 1,
        Object = 2,
        String = 3,
        Number = 4,
        NullValue = 5,
        Boolean = 6,
        None = 7,
        Custom = 0xFF,
    }

    public abstract partial class XMLNode
    {
        #region common interface

        public abstract XMLNodeTypeEX Tag { get; }

        public abstract string Name { get; set; }

        public virtual XMLNode this[int aIndex]
        {
            get { return null; }
            set { }
        }

        public virtual XMLNode this[string aKey]
        {
            get { return null; }
            set { }
        }

        public virtual string Value
        {
            get { return ""; }
            set { }
        }

        public virtual int Count
        {
            get { return 0; }
        }

        public virtual bool IsNumber
        {
            get { return false; }
        }

        public virtual bool IsString
        {
            get { return false; }
        }

        public virtual bool IsBoolean
        {
            get { return false; }
        }

        public virtual bool IsNull
        {
            get { return false; }
        }

        public virtual bool IsArray
        {
            get { return false; }
        }

        public virtual bool IsObject
        {
            get { return false; }
        }

        public virtual void Add(string aKey, XMLNode aItem)
        {
        }

        public virtual void Add(XMLNode aItem)
        {
            Add("", aItem);
        }

        public virtual XMLNode Remove(string aKey)
        {
            return null;
        }

        public virtual XMLNode Remove(int aIndex)
        {
            return null;
        }

        public virtual XMLNode Remove(XMLNode aNode)
        {
            return aNode;
        }

        public virtual void Clear()
        {
        }

        public virtual XMLNode Clone()
        {
            return null;
        }

        public virtual bool HasKey(string aKey)
        {
            return false;
        }

        public virtual XMLNode GetValueOrDefault(string aKey, XMLNode aDefault)
        {
            return aDefault;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            WriteToStringBuilder(sb, 0, 0);
            return sb.ToString();
        }

        internal abstract void WriteToStringBuilder(StringBuilder aSB, int aIndent, int aIndentInc);

        #endregion

        #region typecasting properties

        public virtual double AsDouble
        {
            get
            {
                double v = 0.0;
                if (double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    return v;
                return 0.0;
            }
            set { Value = value.ToString(CultureInfo.InvariantCulture); }
        }

        public virtual int AsInt
        {
            get { return (int)AsDouble; }
            set { AsDouble = value; }
        }

        public virtual float AsFloat
        {
            get { return (float)AsDouble; }
            set { AsDouble = value; }
        }

        public virtual bool AsBool
        {
            get
            {
                bool v = false;
                if (bool.TryParse(Value, out v))
                    return v;
                return !string.IsNullOrEmpty(Value);
            }
            set { Value = value ? "true" : "false"; }
        }

        public virtual long AsLong
        {
            get
            {
                long val = 0;
                if (long.TryParse(Value, out val))
                    return val;
                return 0L;
            }
            set { Value = value.ToString(); }
        }

        public virtual XMLArray AsArray
        {
            get { return this as XMLArray; }
        }

        public virtual XMLObject AsObject
        {
            get { return this as XMLObject; }
        }

        #endregion

        #region ParseMethods

        public static XMLNode Parse(string aXML)
        {
            try
            {
                XDocument doc = XDocument.Parse(aXML);
                return ParseXElement(doc.Root);
            }
            catch (Exception ex)
            {
                throw new Exception("XML Parse error: " + ex.Message);
            }
        }

        private static XMLNode ParseXElement(XElement element)
        {
            string elementName = element.Name.LocalName; // 获取当前元素名称

            // 检查是否是空元素
            if (!element.HasElements && string.IsNullOrEmpty(element.Value))
            {
                var nullNode = XMLNull.CreateOrGet();
                nullNode.Name = elementName; // 设置名称
                return nullNode;
            }

            // 检查是否是混合内容（文本和元素混合）
            if (element.Nodes().Any(n => n is XText) && element.Elements().Any())
            {
                // 混合内容视为字符串
                var strNode = new XMLString(element.Value);
                strNode.Name = elementName; // 设置名称
                return strNode;
            }

            // 检查是否只有文本内容
            if (!element.HasElements && !string.IsNullOrEmpty(element.Value))
            {
                string value = element.Value.Trim();

                // 尝试解析为布尔值
                if (bool.TryParse(value, out bool boolResult))
                {
                    var boolNode = new XMLBool(boolResult);
                    boolNode.Name = elementName; // 设置名称
                    return boolNode;
                }

                // 尝试解析为数字
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleResult))
                {
                    var numNode = new XMLNumber(doubleResult);
                    numNode.Name = elementName; // 设置名称
                    return numNode;
                }

                // 默认作为字符串
                var stringNode = new XMLString(value);
                stringNode.Name = elementName; // 设置名称
                return stringNode;
            }

            // 检查子元素是否都是相同名称（表示数组）
            var childElements = element.Elements().ToList();
            if (childElements.Count > 0)
            {
                string firstName = childElements[0].Name.LocalName;
                bool allSameName = childElements.All(e => e.Name.LocalName == firstName);

                if (allSameName && childElements.Count >= 1)
                {
                    // 作为数组处理
                    XMLArray array = new XMLArray();
                    array.Name = elementName; // 设置数组节点自身的名称（父元素名称）
                    foreach (var child in childElements)
                    {
                        var childNode = ParseXElement(child); // 子节点会在递归中设置自己的名称（如"number"）
                        array.Add(childNode);
                    }

                    return array;
                }
                else
                {
                    // 作为对象处理
                    XMLObject obj = new XMLObject();
                    obj.Name = elementName; // 设置对象节点名称
                    foreach (var child in childElements)
                    {
                        var childNode = ParseXElement(child);
                        obj.Add(child.Name.LocalName, childNode);
                    }

                    // 添加属性
                    foreach (var attr in element.Attributes())
                    {
                        var attrNode = new XMLString(attr.Value);
                        attrNode.Name = $"@{attr.Name.LocalName}"; // 属性节点名称带@前缀
                        obj.Add("@" + attr.Name.LocalName, attrNode);
                    }

                    return obj;
                }
            }

            // 默认作为空节点
            var defaultNull = XMLNull.CreateOrGet();
            defaultNull.Name = elementName; // 设置名称
            return defaultNull;
        }

        #endregion
    }
    // End of XMLNode

    public partial class XMLArray : XMLNode, IEnumerable<XMLNode>
    {
        private List<XMLNode> m_List = new List<XMLNode>();

        public override XMLNodeTypeEX Tag
        {
            get { return XMLNodeTypeEX.Array; }
        }

        private string _name;

        public override string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public override bool IsArray
        {
            get { return true; }
        }

        public override XMLNode this[int aIndex]
        {
            get
            {
                if (aIndex < 0 || aIndex >= m_List.Count)
                    return new XMLLazyCreator(this);
                return m_List[aIndex];
            }
            set
            {
                if (value == null)
                    value = XMLNull.CreateOrGet();
                if (aIndex < 0 || aIndex >= m_List.Count)
                    m_List.Add(value);
                else
                    m_List[aIndex] = value;
            }
        }

        public override int Count
        {
            get { return m_List.Count; }
        }

        public override void Add(string aKey, XMLNode aItem)
        {
            if (aItem == null)
                aItem = XMLNull.CreateOrGet();
            m_List.Add(aItem);
        }

        public override XMLNode Remove(int aIndex)
        {
            if (aIndex < 0 || aIndex >= m_List.Count)
                return null;
            XMLNode tmp = m_List[aIndex];
            m_List.RemoveAt(aIndex);
            return tmp;
        }

        public override void Clear()
        {
            m_List.Clear();
        }

        public override XMLNode Clone()
        {
            XMLArray node = new XMLArray();
            foreach (var n in m_List)
            {
                if (n != null)
                    node.Add(n.Clone());
                else
                    node.Add(null);
            }

            return node;
        }

        internal override void WriteToStringBuilder(StringBuilder aSB, int aIndent, int aIndentInc)
        {
            aSB.Append("<array>");
            for (int i = 0; i < m_List.Count; i++)
            {
                if (i > 0)
                    aSB.AppendLine();
                aSB.Append(' ', aIndent + aIndentInc);
                aSB.Append("<item>");
                m_List[i].WriteToStringBuilder(aSB, aIndent + aIndentInc, aIndentInc);
                aSB.Append("</item>");
            }

            if (m_List.Count > 0)
            {
                aSB.AppendLine();
                aSB.Append(' ', aIndent);
            }

            aSB.Append("</array>");
        }

        public IEnumerator<XMLNode> GetEnumerator()
        {
            return m_List.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // Children 属性以便遍历
        public IEnumerable<XMLNode> Children
        {
            get
            {
                foreach (var node in m_List)
                    yield return node;
            }
        }
    }
    // End of XMLArray

    public partial class XMLObject : XMLNode
    {
        private Dictionary<string, XMLNode> m_Dict = new Dictionary<string, XMLNode>();

        public override XMLNodeTypeEX Tag
        {
            get { return XMLNodeTypeEX.Object; }
        }

        private string _name;

        public override string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public override bool IsObject
        {
            get { return true; }
        }

        public IEnumerable<string> Keys => m_Dict.Keys;

        public override XMLNode this[string aKey]
        {
            get
            {
                if (m_Dict.TryGetValue(aKey, out XMLNode node))
                    return node;
                return new XMLLazyCreator(this, aKey);
            }
            set
            {
                if (value == null)
                    value = XMLNull.CreateOrGet();
                if (m_Dict.ContainsKey(aKey))
                    m_Dict[aKey] = value;
                else
                    m_Dict.Add(aKey, value);
            }
        }

        public override int Count
        {
            get { return m_Dict.Count; }
        }

        public override void Add(string aKey, XMLNode aItem)
        {
            if (aItem == null)
                aItem = XMLNull.CreateOrGet();

            if (aKey != null)
            {
                if (m_Dict.ContainsKey(aKey))
                    m_Dict[aKey] = aItem;
                else
                    m_Dict.Add(aKey, aItem);
            }
            else
            {
                m_Dict.Add(Guid.NewGuid().ToString(), aItem);
            }
        }

        public override XMLNode Remove(string aKey)
        {
            if (!m_Dict.ContainsKey(aKey))
                return null;
            XMLNode tmp = m_Dict[aKey];
            m_Dict.Remove(aKey);
            return tmp;
        }

        public override void Clear()
        {
            m_Dict.Clear();
        }

        public override XMLNode Clone()
        {
            XMLObject node = new XMLObject();
            foreach (var n in m_Dict)
            {
                node.Add(n.Key, n.Value.Clone());
            }

            return node;
        }

        public override bool HasKey(string aKey)
        {
            return m_Dict.ContainsKey(aKey);
        }

        internal override void WriteToStringBuilder(StringBuilder aSB, int aIndent, int aIndentInc)
        {
            aSB.Append("<object>");
            bool first = true;
            foreach (var k in m_Dict)
            {
                if (!first)
                    aSB.AppendLine();
                first = false;

                aSB.Append(' ', aIndent + aIndentInc);
                aSB.Append('<').Append(k.Key).Append('>');
                k.Value.WriteToStringBuilder(aSB, aIndent + aIndentInc, aIndentInc);
                aSB.Append("</").Append(k.Key).Append('>');
            }

            if (m_Dict.Count > 0)
            {
                aSB.AppendLine();
                aSB.Append(' ', aIndent);
            }

            aSB.Append("</object>");
        }
    }
    // End of XMLObject

    public partial class XMLString : XMLNode
    {
        private string m_Data;

        public override XMLNodeTypeEX Tag
        {
            get { return XMLNodeTypeEX.String; }
        }

        private string _name;

        public override string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public override bool IsString
        {
            get { return true; }
        }

        public override string Value
        {
            get { return m_Data; }
            set { m_Data = value; }
        }

        public XMLString(string aData)
        {
            m_Data = aData;
        }

        public override XMLNode Clone()
        {
            return new XMLString(m_Data);
        }

        internal override void WriteToStringBuilder(StringBuilder aSB, int aIndent, int aIndentInc)
        {
            aSB.Append(XML.Escape(m_Data));
        }
    }
    // End of XMLString

    public partial class XMLNumber : XMLNode
    {
        private double m_Data;

        public override XMLNodeTypeEX Tag
        {
            get { return XMLNodeTypeEX.Number; }
        }

        private string _name;

        public override string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public override bool IsNumber
        {
            get { return true; }
        }

        public override string Value
        {
            get { return m_Data.ToString(CultureInfo.InvariantCulture); }
            set
            {
                double v;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    m_Data = v;
            }
        }

        public override double AsDouble
        {
            get { return m_Data; }
            set { m_Data = value; }
        }

        public XMLNumber(double aData)
        {
            m_Data = aData;
        }

        public override XMLNode Clone()
        {
            return new XMLNumber(m_Data);
        }

        internal override void WriteToStringBuilder(StringBuilder aSB, int aIndent, int aIndentInc)
        {
            aSB.Append(Value);
        }
    }
    // End of XMLNumber

    public partial class XMLBool : XMLNode
    {
        private bool m_Data;

        public override XMLNodeTypeEX Tag
        {
            get { return XMLNodeTypeEX.Boolean; }
        }

        private string _name;

        public override string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public override bool IsBoolean
        {
            get { return true; }
        }

        public override string Value
        {
            get { return m_Data.ToString(); }
            set
            {
                bool v;
                if (bool.TryParse(value, out v))
                    m_Data = v;
            }
        }

        public override bool AsBool
        {
            get { return m_Data; }
            set { m_Data = value; }
        }

        public XMLBool(bool aData)
        {
            m_Data = aData;
        }

        public override XMLNode Clone()
        {
            return new XMLBool(m_Data);
        }

        internal override void WriteToStringBuilder(StringBuilder aSB, int aIndent, int aIndentInc)
        {
            aSB.Append(m_Data ? "true" : "false");
        }
    }
    // End of XMLBool

    public partial class XMLNull : XMLNode
    {
        static XMLNull m_StaticInstance = new XMLNull();
        public static bool reuseSameInstance = true;

        public static XMLNull CreateOrGet()
        {
            if (reuseSameInstance)
                return m_StaticInstance;
            return new XMLNull();
        }

        private XMLNull()
        {
        }

        public override XMLNodeTypeEX Tag
        {
            get { return XMLNodeTypeEX.NullValue; }
        }

        private string _name;

        public override string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public override bool IsNull
        {
            get { return true; }
        }

        public override string Value
        {
            get { return "null"; }
            set { }
        }

        public override XMLNode Clone()
        {
            return CreateOrGet();
        }

        internal override void WriteToStringBuilder(StringBuilder aSB, int aIndent, int aIndentInc)
        {
            aSB.Append("null");
        }
    }
    // End of XMLNull

    internal partial class XMLLazyCreator : XMLNode
    {
        private XMLNode m_Node = null;
        private string m_Key = null;

        public override XMLNodeTypeEX Tag
        {
            get { return XMLNodeTypeEX.None; }
        }

        public override string Name { get; set; } = string.Empty;

        public XMLLazyCreator(XMLNode aNode)
        {
            m_Node = aNode;
            m_Key = null;
        }

        public XMLLazyCreator(XMLNode aNode, string aKey)
        {
            m_Node = aNode;
            m_Key = aKey;
        }

        private T Set<T>(T aVal) where T : XMLNode
        {
            if (m_Key == null)
                m_Node.Add(aVal);
            else
                m_Node.Add(m_Key, aVal);
            m_Node = null;
            return aVal;
        }

        public override XMLNode this[int aIndex]
        {
            get { return new XMLLazyCreator(this); }
            set { Set(new XMLArray()).Add(value); }
        }

        public override XMLNode this[string aKey]
        {
            get { return new XMLLazyCreator(this, aKey); }
            set { Set(new XMLObject()).Add(aKey, value); }
        }

        public override void Add(XMLNode aItem)
        {
            Set(new XMLArray()).Add(aItem);
        }

        public override void Add(string aKey, XMLNode aItem)
        {
            Set(new XMLObject()).Add(aKey, aItem);
        }

        public override int AsInt
        {
            get
            {
                Set(new XMLNumber(0));
                return 0;
            }
            set { Set(new XMLNumber(value)); }
        }

        public override double AsDouble
        {
            get
            {
                Set(new XMLNumber(0.0));
                return 0.0;
            }
            set { Set(new XMLNumber(value)); }
        }

        public override bool AsBool
        {
            get
            {
                Set(new XMLBool(false));
                return false;
            }
            set { Set(new XMLBool(value)); }
        }

        public override XMLArray AsArray
        {
            get { return Set(new XMLArray()); }
        }

        public override XMLObject AsObject
        {
            get { return Set(new XMLObject()); }
        }

        internal override void WriteToStringBuilder(StringBuilder aSB, int aIndent, int aIndentInc)
        {
            aSB.Append("null");
        }
    }
    // End of XMLLazyCreator

    public static class XML
    {
        [ThreadStatic] private static StringBuilder m_EscapeBuilder;

        internal static StringBuilder EscapeBuilder
        {
            get
            {
                if (m_EscapeBuilder == null)
                    m_EscapeBuilder = new StringBuilder();
                return m_EscapeBuilder;
            }
        }

        internal static string Escape(string aText)
        {
            var sb = EscapeBuilder;
            sb.Length = 0;
            if (sb.Capacity < aText.Length + aText.Length / 10)
                sb.Capacity = aText.Length + aText.Length / 10;

            foreach (char c in aText)
            {
                switch (c)
                {
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '&': sb.Append("&amp;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }

            string result = sb.ToString();
            sb.Length = 0;
            return result;
        }

        public static XMLNode Parse(string aXML)
        {
            return XMLNode.Parse(aXML);
        }
    }
}