#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>构建输出交付类型的菜单入口。详细交付行为由 BuildProjectRunner 统一验证。</summary>
public static class BuildDeliveryPromoteSelfCheck
{
    [MenuItem("FYAsset/Tests/AB Delivery Promote Self Check")]
    public static void Run()
    {
        Debug.Log("[BuildDeliveryPromoteSelfCheck] BuildOutputDelivery 已由 BuildProjectRunner 统一拥有。");
    }
}
#endif
