using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAnimationDriver
{
    void Initialize(GameObject owner);
    void Play(string name);
    void Stop();
    void SetInt(string name, int value);
    void SetFloat(string name, float value);
    void SetBool(string name, bool value);
    void SetTrigger(string name);
    void OnUpdate(float deltaTime);
}

