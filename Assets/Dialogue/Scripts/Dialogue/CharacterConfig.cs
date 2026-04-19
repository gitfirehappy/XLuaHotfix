using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterConfig", menuName = "Dialogue/Character Config")]
public class CharacterConfig : ScriptableObject
{
    [Tooltip("CSV中对应的角色名ID")]
    public string CharacterName; 

    [Tooltip("角色宽度")]
    public float Width = 100f;
    
    [Tooltip("角色高度")]
    public float Height = 100f;
    
    [Tooltip("差分图片列表 (索引对应 CSV 中的 diff 参数)")]
    public List<Sprite> Sprites;

    public Sprite GetSprite(int index)
    {
        if (Sprites == null || Sprites.Count == 0) return null;
        if (index < 0 || index >= Sprites.Count) return Sprites[0];
        return Sprites[index];
    }
}

