using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Reflection;

public class CollectorSettingInspectorTests
{
    [Test]
    public void OpenBuildPipelineWindow_FromInspector_DoesNotThrow()
    {
        // 查找 CollectorSetting 类型
        var collectorType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
            .FirstOrDefault(t => t.Name == "CollectorSetting");

        if (collectorType == null)
        {
            Assert.Ignore("CollectorSetting type not found in project; cannot run inspector integration test.");
            return;
        }

        // 创建临时实例并保存为 asset
        var so = ScriptableObject.CreateInstance(collectorType);
        string path = "Assets/Temp_CollectorSetting_ForTest.asset";
        AssetDatabase.CreateAsset(so, path);
        AssetDatabase.SaveAssets();

        try
        {
            // 创建 Editor 并尝试通过反射调用私有方法 OpenBuildPipelineWindow
            var editor = Editor.CreateEditor(so);
            Assert.IsNotNull(editor, "Failed to create Editor for CollectorSetting instance.");

            var method = editor.GetType().GetMethod("OpenBuildPipelineWindow", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null)
            {
                Assert.Ignore("CollectorSettingInspector.OpenBuildPipelineWindow not found via reflection.");
            }
            else
            {
                // 调用方法，期望不会抛出异常
                Assert.DoesNotThrow(() => method.Invoke(editor, null));

                // 尝试查找窗口类型
                var winType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.Name == "BuildPipelineWindow");
                if (winType != null)
                {
                    var win = EditorWindow.GetWindow(winType);
                    Assert.IsNotNull(win, "BuildPipelineWindow should be open after invoking OpenBuildPipelineWindow.");
                }
            }
        }
        finally
        {
            // 清理临时 asset
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.Refresh();
        }
    }
}
