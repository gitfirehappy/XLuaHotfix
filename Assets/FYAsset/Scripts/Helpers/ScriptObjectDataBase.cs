using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ScriptObjectDataBase : ScriptableObject
{
    [Tooltip("所有 SOContainer 资源的引用")]
    public List<ScriptObjectContainer> groups = new();

    public IEnumerable<ScriptObjectContainer> AllGroups => groups;
}
