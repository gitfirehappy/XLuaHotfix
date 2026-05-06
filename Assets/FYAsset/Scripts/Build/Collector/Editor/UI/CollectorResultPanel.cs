using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 底部结果区域渲染辅助类：校验消息 + 扫描预览。
/// </summary>
public static class CollectorResultPanel
{
    private static Vector2 s_validationScroll;
    private static Vector2 s_scanScroll;

    private static ScanResult s_cachedScanResult;
    private static string s_cachedScanText;

    public static void Render(Rect rect, List<BuildMessage> validationMessages, ScanResult scanResult, bool isScanning, bool showValidation)
    {
        if (rect.height <= 0 || rect.width <= 0) return;

        if (isScanning)
        {
            GUI.Label(rect, "Scanning...", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        if (showValidation)
            RenderValidation(rect, validationMessages);
        else
            RenderScanPreview(rect, scanResult);
    }

    /// <summary>渲染校验消息列表：Severity / Code / Message 三列</summary>
    private static void RenderValidation(Rect rect, List<BuildMessage> messages)
    {
        if (messages == null || messages.Count == 0)
        {
            GUI.Label(rect, "No validation messages.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        // Estimate content height for scroll view inner rect
        float rowHeight = EditorGUIUtility.singleLineHeight + 2f;
        float contentHeight = messages.Count * rowHeight;
        Rect viewRect = new Rect(0, 0, rect.width - 16f, contentHeight);

        s_validationScroll = GUI.BeginScrollView(rect, s_validationScroll, viewRect);
        float y = 0f;
        foreach (var m in messages)
        {
            Rect rowRect = new Rect(0, y, viewRect.width, EditorGUIUtility.singleLineHeight);
            GUIStyle style = m.Severity == BuildSeverity.Error ? EditorStyles.boldLabel : EditorStyles.miniLabel;
            GUI.Label(new Rect(rowRect.x, rowRect.y, 60, rowRect.height), m.Severity.ToString(), style);
            GUI.Label(new Rect(rowRect.x + 64, rowRect.y, 220, rowRect.height), m.Code, EditorStyles.miniLabel);
            GUI.Label(new Rect(rowRect.x + 288, rowRect.y, viewRect.width - 288, rowRect.height), m.Message, style);
            y += rowHeight;
        }
        GUI.EndScrollView();
    }

    private static GUIStyle s_textAreaStyle;

    private static GUIStyle GetTextAreaStyle()
    {
        if (s_textAreaStyle == null)
        {
            s_textAreaStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                richText = false,
                stretchHeight = true,
            };
        }
        return s_textAreaStyle;
    }

    /// <summary>渲染扫描预览：资产数量 + 资产路径列表（缓存避免每帧重建字符串）</summary>
    private static void RenderScanPreview(Rect rect, ScanResult result)
    {
        if (result == null)
        {
            GUI.Label(rect, "No scan executed.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        int assetCount = result.Assets?.Count ?? 0;

        float headerHeight = EditorGUIUtility.singleLineHeight + 2f;
        Rect headerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
        GUI.Label(headerRect, $"Assets: {assetCount}", EditorStyles.boldLabel);

        Rect textAreaRect = new Rect(rect.x, rect.y + headerHeight, rect.width, rect.height - headerHeight);

        if (assetCount == 0)
        {
            GUI.Label(textAreaRect, "No assets collected.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        if (!ReferenceEquals(result, s_cachedScanResult))
        {
            s_cachedScanResult = result;
            var sb = new StringBuilder();
            foreach (var a in result.Assets)
                sb.AppendLine(a.AssetPath);
            s_cachedScanText = sb.ToString();
        }

        GUI.TextArea(textAreaRect, s_cachedScanText, GetTextAreaStyle());
    }
}
