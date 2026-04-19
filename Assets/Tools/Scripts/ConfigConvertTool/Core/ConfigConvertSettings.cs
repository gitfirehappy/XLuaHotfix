using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Config/ConfigConvertSetting")]
public class ConfigConvertSettings : ScriptableObject
{
    public List<ConfigConvertChannel> channels;
}

