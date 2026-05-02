using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Helper for rendering the bottom result band: Validation messages and Scan Preview results.
/// Kept small and editor-only; purely presentational.
/// </summary>
public static class CollectorResultPanel
{
    // Render accepts a simple bool to indicate whether to show Validation (true) or ScanPreview (false).
    public static void Render(Rect rect, List<BuildMessage> validationMessages, ScanResult scanResult, bool isScanning, bool showValidation)
    {
        if (rect.height <= 0 || rect.width <= 0) return;

        GUILayout.BeginArea(rect);

        if (isScanning)
        {
            GUILayout.Label("Scanning...", EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndArea();
            return;
        }

        if (showValidation)
        {
            RenderValidation(validationMessages);
        }
        else
        {
            RenderScanPreview(scanResult);
        }

        GUILayout.EndArea();
    }

    private static void RenderValidation(List<BuildMessage> messages)
    {
        if (messages == null || messages.Count == 0)
        {
            GUILayout.Label("No validation messages.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        GUILayout.BeginVertical();
        foreach (var m in messages)
        {
            EditorGUILayout.BeginHorizontal();
            GUIStyle style = m.Severity == BuildSeverity.Error ? EditorStyles.boldLabel : EditorStyles.label;
            GUILayout.Label(m.Severity.ToString(), GUILayout.Width(60));
            GUILayout.Label(m.Code, GUILayout.Width(220));
            GUILayout.Label(m.Message, style);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }

    private static void RenderScanPreview(ScanResult result)
    {
        if (result == null)
        {
            GUILayout.Label("No scan executed.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        GUILayout.BeginVertical();
        GUILayout.Label($"Assets: {result.Assets?.Count ?? 0}", EditorStyles.boldLabel);
        if (result.Messages != null && result.Messages.Count > 0)
        {
            GUILayout.Label("Messages:");
            foreach (var m in result.Messages)
                GUILayout.Label($"[{m.Severity}] {m.Message}", EditorStyles.wordWrappedLabel);
        }

        if (result.Assets != null && result.Assets.Count > 0)
        {
            GUILayout.Space(6);
            GUILayout.Label("Sample assets:");
            int limit = Mathf.Min(10, result.Assets.Count);
            for (int i = 0; i < limit; i++)
                GUILayout.Label(result.Assets[i].AssetPath, EditorStyles.miniLabel);
        }

        GUILayout.EndVertical();
    }
}
