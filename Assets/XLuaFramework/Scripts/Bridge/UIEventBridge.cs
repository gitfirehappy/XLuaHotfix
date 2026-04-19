using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using XLua;

[LuaCallCSharp]
public class UIEventBridge : MonoBehaviour, IBridge,
    IPointerClickHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IScrollHandler
{
    private LuaTable luaInstance; // 从IBridge初始化传入

    public async Task InitializeAsync(LuaTable luaInstance)
    {
        this.luaInstance = luaInstance;
        await Task.CompletedTask;
    }
    
    private void CallLua(string methodName, BaseEventData data)
    {
        if (luaInstance == null) return;
        var func = luaInstance.Get<LuaFunction>(methodName);
        if (func != null)
        {
            func.Call(luaInstance, data); // self + 参数
        }
    }

    public void OnPointerClick(PointerEventData eventData) => CallLua("OnPointerClick", eventData);
    public void OnPointerDown(PointerEventData eventData) => CallLua("OnPointerDown", eventData);
    public void OnPointerUp(PointerEventData eventData) => CallLua("OnPointerUp", eventData);
    public void OnPointerEnter(PointerEventData eventData) => CallLua("OnPointerEnter", eventData);
    public void OnPointerExit(PointerEventData eventData) => CallLua("OnPointerExit", eventData);
    public void OnBeginDrag(PointerEventData eventData) => CallLua("OnBeginDrag", eventData);
    public void OnDrag(PointerEventData eventData) => CallLua("OnDrag", eventData);
    public void OnEndDrag(PointerEventData eventData) => CallLua("OnEndDrag", eventData);
    public void OnScroll(PointerEventData eventData) => CallLua("OnScroll", eventData);
}