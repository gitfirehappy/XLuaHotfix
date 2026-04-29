using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SODataBase", menuName = "Addressables/MyWork/SO Database")]
public class ScriptObjectDataBse : ScriptableObject
{
    [Tooltip("所有 SOContainer 资源的引用")]
    public List<ScriptObjectContainer> groups = new();

    public IEnumerable<ScriptObjectContainer> AllGroups => groups;
}
