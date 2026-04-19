using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AttributeUsage(AttributeTargets.Method,Inherited = false,AllowMultiple = false)]
public class DialogueFuncAttribute : Attribute
{
    public string DisplayName { get; set; }
    
    public DialogueFuncAttribute() { }
    
    public DialogueFuncAttribute(string name)
    {
        DisplayName = name;
    }
}
