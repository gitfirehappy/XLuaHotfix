using System.Text;
using UnityEngine;

/// <summary>
/// JsonUtility 编解码器封装。
/// </summary>
public sealed class JsonCodec : ISerializationCodec
{
    public const string JsonCodecId = "json";

    public string CodecId => JsonCodecId;

    public byte[] Serialize<T>(T obj, bool prettyPrint = false)
    {
        string json = JsonUtility.ToJson(obj, prettyPrint);
        return Encoding.UTF8.GetBytes(json);
    }

    public T Deserialize<T>(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return default;
        }

        string json = Encoding.UTF8.GetString(data);
        if (json.Length > 0 && json[0] == '\uFEFF')
            json = json.Substring(1);
        return JsonUtility.FromJson<T>(json);
    }
}
