using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StateAnimationConfig", menuName = "XLua/Bridge/State Animation Config")]
public class StateAnimationConfigSO : ScriptableObject
{
    [Serializable]
    public class StateAnimationMapping
    {
        [Tooltip("状态路径，用'/'分隔，如: Grounded/Idle, Airborne/Jump")]
        public string statePath;
        
        [Tooltip("对应的动画片段")]
        public AnimationClip animationClip;
        
        [Tooltip("动画过渡时间")]
        public float transitionDuration = 0.1f;
    }

    [Header("状态动画映射")]
    public List<StateAnimationMapping> stateAnimations = new List<StateAnimationMapping>();

    [Header("默认动画")]
    public AnimationClip defaultAnimation;

    /// <summary>
    /// 根据状态路径获取动画配置
    /// </summary>
    public StateAnimationMapping GetAnimationMapping(string statePath)
    {
        foreach (var mapping in stateAnimations)
        {
            if (mapping.statePath == statePath)
                return mapping;
        }
        return null;
    }

    /// <summary>
    /// 获取动画片段
    /// </summary>
    public AnimationClip GetAnimationClip(string statePath)
    {
        var mapping = GetAnimationMapping(statePath);
        return mapping?.animationClip ?? defaultAnimation;
    }

    /// <summary>
    /// 检查是否包含指定状态路径的动画
    /// </summary>
    public bool HasAnimationForState(string statePath)
    {
        return GetAnimationMapping(statePath) != null;
    }
}