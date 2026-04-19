using System.Collections;
using System.Collections.Generic;
using SimpleXML;
using UnityEngine;

public class SimpleXMLTester : MonoBehaviour
{
    [ContextMenu("RunAllTests")]
    void RunAllTests()
    {
        Debug.Log("=== SimpleXML 测试开始 ===");

        TestBasicObject();
        TestBasicArray();
        TestMixedContent();
        TestNestedStructures();
        TestDataTypes();
        TestAttributes();
        TestEmptyAndNull();

        Debug.Log("=== SimpleXML 测试结束 ===");
    }

    [ContextMenu("测试 1: 基本对象解析")]
    void TestBasicObject()
    {
        Debug.Log("测试 1: 基本对象解析");
        string xml = @"<player><name>John</name><level>5</level><active>true</active></player>";

        XMLNode node = XML.Parse(xml);
        Debug.Assert(node.IsObject, "应该解析为对象");
        Debug.Assert(node["name"].Value == "John", "name 字段应该为 'John'");
        Debug.Assert(node["level"].AsInt == 5, "level 字段应该为 5");
        Debug.Assert(node["active"].AsBool, "active 字段应该为 true");

        Debug.Log("✓ 基本对象测试通过");
    }

    [ContextMenu("测试 2: 基本数组解析")]
    void TestBasicArray()
    {
        Debug.Log("测试 2: 基本数组解析");
        string xml = @"<items><item>Apple</item><item>Banana</item><item>Orange</item></items>";

        XMLNode node = XML.Parse(xml);
        Debug.Assert(node.IsArray, "应该解析为数组");
        Debug.Assert(node.Count == 3, "数组应该包含 3 个元素");
        Debug.Assert(node[0].Value == "Apple", "第一个元素应该为 'Apple'");
        Debug.Assert(node[1].Value == "Banana", "第二个元素应该为 'Banana'");
        Debug.Assert(node[2].Value == "Orange", "第三个元素应该为 'Orange'");

        Debug.Log("✓ 基本数组测试通过");
    }

    [ContextMenu("测试 3: 混合内容处理")]
    void TestMixedContent()
    {
        Debug.Log("测试 3: 混合内容处理");
        string xml = @"<message>Hello <b>world</b>! How are you <i>today</i>?</message>";

        XMLNode node = XML.Parse(xml);
        // 根据你的实现，混合内容应该被视为字符串
        Debug.Assert(node.IsString, "混合内容应该被视为字符串");
        Debug.Log($"混合内容结果: {node.Value}");

        Debug.Log("✓ 混合内容测试通过");
    }

    [ContextMenu("测试 4: 嵌套结构解析")]
    void TestNestedStructures()
    {
        Debug.Log("测试 4: 嵌套结构解析");
        string xml = @"
<game>
    <players>
        <player>
            <name>John</name>
            <inventory>
                <item>Sword</item>
                <item>Shield</item>
            </inventory>
        </player>
        <player>
            <name>Jane</name>
            <inventory>
                <item>Bow</item>
                <item>Arrows</item>
            </inventory>
        </player>
    </players>
</game>";

        XMLNode node = XML.Parse(xml);
        Debug.Assert(node.IsObject, "根节点应该是对象");
        Debug.Assert(node["players"].IsArray, "players 应该是数组");
        Debug.Assert(node["players"].Count == 2, "应该有两个玩家");

        XMLNode firstPlayer = node["players"][0];
        Debug.Assert(firstPlayer["name"].Value == "John", "第一个玩家名字应该是 John");
        Debug.Assert(firstPlayer["inventory"].IsArray, "库存应该是数组");
        Debug.Assert(firstPlayer["inventory"].Count == 2, "库存应该有两个物品");
        Debug.Assert(firstPlayer["inventory"][0].Value == "Sword", "第一个物品应该是 Sword");

        Debug.Log("✓ 嵌套结构测试通过");
    }

    [ContextMenu("测试 5: 数据类型检测")]
    void TestDataTypes()
    {
        Debug.Log("测试 5: 数据类型检测");
        string xml = @"
<data>
    <string>Hello World</string>
    <integer>42</integer>
    <float>3.14</float>
    <boolean>true</boolean>
    <negative>-5</negative>
    <scientific>1.2e-3</scientific>
</data>";

        XMLNode node = XML.Parse(xml);
        Debug.Assert(node["string"].IsString, "应该检测为字符串");
        Debug.Assert(node["integer"].IsNumber, "应该检测为数字");
        Debug.Assert(node["integer"].AsInt == 42, "整数值应该为 42");
        Debug.Assert(node["float"].AsFloat == 3.14f, "浮点值应该为 3.14");
        Debug.Assert(node["boolean"].AsBool, "布尔值应该为 true");
        Debug.Assert(node["negative"].AsInt == -5, "负数值应该为 -5");
        Debug.Assert(node["scientific"].AsFloat == 0.0012f, "科学计数法值应该正确解析");

        Debug.Log("✓ 数据类型测试通过");
    }

    [ContextMenu("测试 6: 属性处理")]
    void TestAttributes()
    {
        Debug.Log("测试 6: 属性处理");
        string xml = @"<player id='123' type='warrior' active='true'><name>John</name></player>";

        XMLNode node = XML.Parse(xml);
        Debug.Assert(node.IsObject, "应该解析为对象");
        Debug.Assert(node["@id"].Value == "123", "id 属性应该为 '123'");
        Debug.Assert(node["@type"].Value == "warrior", "type 属性应该为 'warrior'");
        Debug.Assert(node["@active"].AsBool, "active 属性应该为 true");
        Debug.Assert(node["name"].Value == "John", "name 元素应该为 'John'");

        Debug.Log("✓ 属性测试通过");
    }

    [ContextMenu("测试 7: 空值和空元素处理")]
    void TestEmptyAndNull()
    {
        Debug.Log("测试 7: 空值和空元素处理");
        string xml = @"
<data>
    <empty></empty>
    <selfClosing/>
    <null>null</null>
    <whitespace>   </whitespace>
</data>";

        XMLNode node = XML.Parse(xml);
        Debug.Assert(node["empty"].IsNull, "空元素应该解析为 null");
        Debug.Assert(node["selfClosing"].IsNull, "自闭合元素应该解析为 null");
        Debug.Assert(node["null"].IsNull, "显式 null 应该解析为 null");
        Debug.Assert(node["whitespace"].IsString, "空白内容应该解析为字符串");
        Debug.Assert(string.IsNullOrWhiteSpace(node["whitespace"].Value), "空白内容应该保留");

        Debug.Log("✓ 空值和空元素测试通过");
    }
}