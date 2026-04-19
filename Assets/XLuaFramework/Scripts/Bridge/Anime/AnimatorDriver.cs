using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimatorDriver : IAnimationDriver
{
    public enum ControlMode
    {
        GraphDriven,  // 使用 AnimatorController 的连线逻辑
        CodeDriven,   // 代码设置状态机，直接 Play 动画片段
    }

    private Animator animator;
    private ControlMode mode;

    public AnimatorDriver(ControlMode mode = ControlMode.GraphDriven)
    {
        this.mode = mode;
    }

    public void Initialize(GameObject owner)
    {
        animator = owner.GetComponent<Animator>();
        if (animator == null)
            Debug.LogError("[AnimatorDriver] Animator component missing!");
    }

    public void Play(string state)
    {
        if (animator == null) return;

        if (mode == ControlMode.GraphDriven)
        {
            animator.SetTrigger(state);
        }
        else if (mode == ControlMode.CodeDriven)
        {
            animator.Play(state, 0, 0f);
        }
    }

    public void Stop()
    {
        if (animator == null) return;
        animator.Rebind();
    }

    public void SetFloat(string name, float value) => animator?.SetFloat(name, value);
    public void SetBool(string name, bool value) => animator?.SetBool(name, value);
    public void SetTrigger(string name) => animator?.SetTrigger(name);
    public void SetInt(string name, int value) => animator?.SetInteger(name, value);

    public void OnUpdate(float deltaTime) { }
}

