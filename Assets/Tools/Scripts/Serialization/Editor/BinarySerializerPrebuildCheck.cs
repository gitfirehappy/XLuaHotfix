using System;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>
/// 构建前检查生成文件是否过期。
/// </summary>
public sealed class BinarySerializerPrebuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 10;

    public void OnPreprocessBuild(BuildReport report)
    {
        var fieldIssues = BinarySerializerGenerator.GetFieldIssues();
        var stale = BinarySerializerGenerator.GetStaleTypes();
        if (fieldIssues.Count == 0 && stale.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        if (fieldIssues.Count > 0)
        {
            sb.Append(BinarySerializerGenerator.BuildFieldIssueMessage(fieldIssues));
            sb.AppendLine();
        }

        if (stale.Count > 0)
        {
            sb.AppendLine("以下 Binary serializer 已过期，请先重新生成：");
            for (int i = 0; i < stale.Count; i++)
            {
                sb.Append("- ").AppendLine(stale[i].FullName);
            }
            sb.AppendLine("可执行菜单：Tools/Serialization/Generate Binary Serializers");
        }

        throw new BuildFailedException(sb.ToString());
    }
}
