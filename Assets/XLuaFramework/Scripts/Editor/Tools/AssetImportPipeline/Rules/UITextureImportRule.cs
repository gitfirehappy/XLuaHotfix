using UnityEditor;

/// <summary>
/// 示例规则：Assets/AboutXLua/Art/UI/ 目录下的图片自动设为 Sprite + 关闭 Mipmap。
/// </summary>
public class UITextureImportRule : AssetImportRuleBase
{
    public override string RuleName => "UI纹理自动设置";

    public override bool Match(string assetPath)
    {
        return assetPath.StartsWith("Assets/AboutXLua/Art/UI/")
               && (assetPath.EndsWith(".png")
                   || assetPath.EndsWith(".jpg")
                   || assetPath.EndsWith(".tga"));
    }

    public override void OnPreprocess(AssetImporter importer)
    {
        if (importer is TextureImporter textureImporter)
        {
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.mipmapEnabled = false;
            textureImporter.spritePixelsPerUnit = 100;
        }
    }
}
