using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// UI 管理器（单例）
/// 负责动态加载、注册、显示、隐藏、回收 UI 面板
/// </summary>
public class UIManager : Singleton<UIManager>
{
    #region 字段

    /// <summary> 路径映射模板（Key = 面板名，Value = Resources 路径） </summary>
    public Dictionary<string, GameObject> formPrefabs = new();

    /// <summary> 当前已注册的实例面板 </summary>
    public Dictionary<string, UIFormBase> forms = new();

    /// <summary> 当前正在显示的面板 </summary>
    public List<UIFormBase> showForms = new();

    /// <summary> 面板显示堆栈（用于顺序关闭） </summary>
    public Stack<UIFormBase> showFormStack = new();

    /// <summary>动态面板分组字典</summary>
    public Dictionary<string, List<UIFormBase>> dynamicFormGroups = new Dictionary<string, List<UIFormBase>>();

    /// <summary> Canvas分组映射（Key = groupID, Value = Canvas） </summary>
    private Dictionary<string, Canvas> canvasGroups = new Dictionary<string, Canvas>();

    /// <summary> UI表单与Canvas的映射（Key = 类型名, Value = Canvas） </summary>
    private Dictionary<string, Canvas> formToCanvasMap = new Dictionary<string, Canvas>();


    #endregion

    #region 初始化(可给管理器调用)

    /// <summary>
    /// 初始化UI管理器
    /// </summary>
    public void Initialize(UIResourceConfigSO config)
    {
        // 清空所有映射
        canvasGroups.Clear();
        formToCanvasMap.Clear();

        // 注册Canvas分组和表单映射
        foreach (var registrationGroup in config.uiRegistrationGroups)
        {
            if (string.IsNullOrEmpty(registrationGroup.parentCanvasName))
            {
                Debug.LogError("UIResourceConfigSO contains empty parentCanvasName!");
                continue;
            }

            // 通过名称查找场景中的Canvas
            Canvas targetCanvas = FindCanvasByName(registrationGroup.parentCanvasName);
            if (targetCanvas == null)
            {
                Debug.LogError($"Canvas named '{registrationGroup.parentCanvasName}' not found in scene!");
                continue;
            }

            foreach (var uiGroup in registrationGroup.uiGroups)
            {
                if (string.IsNullOrEmpty(uiGroup.groupID)) continue;

                // 注册Canvas组（允许多个分组ID对应同一个Canvas）
                RegisterCanvasGroup(uiGroup.groupID, targetCanvas);

                // 为所有静态UI表单建立Canvas映射
                MapFormsToCanvas(uiGroup.manualUIForms, targetCanvas);
                MapFormsToCanvas(uiGroup.dynamicFormTemplates, targetCanvas);

                // 预加载资源
                PreLoadForms(uiGroup.manualUIForms);
                PreLoadForms(uiGroup.dynamicFormTemplates);
            }
        }
    }

    /// <summary>
    /// 为UI表单建立Canvas映射
    /// </summary>
    private void MapFormsToCanvas(GameObject[] prefabs, Canvas canvas)
    {
        foreach (var prefab in prefabs)
        {
            if (prefab == null) continue;

            var formBase = prefab.GetComponent<UIFormBase>();
            if (formBase == null)
            {
                Debug.LogError($"Prefab {prefab.name} does not have UIFormBase component!");
                continue;
            }

            string className = formBase.GetType().Name;
            if (!formToCanvasMap.ContainsKey(className))
            {
                formToCanvasMap.Add(className, canvas);
            }
        }
    }

    #endregion

    #region Canvas管理

    /// <summary>
    /// 注册Canvas分组
    /// </summary>
    public void RegisterCanvasGroup(string groupID, Canvas canvas)
    {
        if (!canvasGroups.ContainsKey(groupID))
        {
            canvasGroups.Add(groupID, canvas);
            Debug.Log($"Registered canvas group: {groupID} -> {canvas.name}");
        }
    }

    /// <summary>
    /// 获取Canvas组
    /// </summary>
    public Canvas GetCanvasGroup(string groupID)
    {
        if (canvasGroups.TryGetValue(groupID, out Canvas canvas))
        {
            return canvas;
        }
        return null;
    }

    /// <summary>
    /// 获取UI表单对应的Canvas
    /// </summary>
    public Canvas GetCanvasForForm(string className)
    {
        if (formToCanvasMap.TryGetValue(className, out Canvas canvas))
        {
            return canvas;
        }
        return null;
    }

    /// <summary>
    /// 通过名称查找场景中的Canvas
    /// </summary>
    /// <param name="canvasName"></param>
    /// <returns></returns>
    private Canvas FindCanvasByName(string canvasName)
    {
        GameObject canvasObj = GameObject.Find(canvasName);
        return canvasObj?.GetComponent<Canvas>();
    }

    #endregion


    #region 注册接口

    /// <summary>
    /// 普通面板注册接口
    /// </summary>
    /// <param name="uIForm"></param>
    public void RegisterForm(IUIForm uIForm)
    {
        var form = uIForm.GetUIFormBase();
        if (form.IsDynamicForm)
        {
            Debug.LogError($"Cannot register dynamic form {form.name} using RegisterForm, use RegisterDynamicForm instead");
            return;
        }

        string key = form.GetType().Name;
        RegisterFormInternal(key, form);
    }

    /// <summary>
    /// 动态面板注册接口
    /// </summary>
    /// <param name="uIForm"></param>
    /// <param name="groupID"></param>
    public void RegisterDynamicForm(IUIForm uIForm, string groupID)
    {
        var form = uIForm.GetUIFormBase();
        if (!form.IsDynamicForm)
        {
            Debug.LogError($"Cannot register non-dynamic form {form.name} using RegisterDynamicForm");
            return;
        }

        string key = $"{groupID}_{Guid.NewGuid()}";
        form.InitializeAsDynamic(groupID);
        RegisterFormInternal(key, form);
    }

    /// <summary>
    /// 内部注册方法
    /// </summary>
    /// <param name="key"></param>
    /// <param name="form"></param>
    private void RegisterFormInternal(string key, UIFormBase form)
    {
        if (!forms.ContainsKey(key))
        {
            forms.Add(key, form);

            // 动态面板分组处理
            if (form.IsDynamicForm && !string.IsNullOrEmpty(form.DynamicGroupID))
            {
                if (!dynamicFormGroups.ContainsKey(form.DynamicGroupID))
                {
                    dynamicFormGroups.Add(form.DynamicGroupID, new List<UIFormBase>());
                }
                dynamicFormGroups[form.DynamicGroupID].Add(form);
            }

            form.Close(); // 默认关闭
        }
    }

    public void UnRegisterForm(IUIForm uIForm)
    {
        var form = uIForm.GetUIFormBase();
        string key = form.GetType().Name;

        // 如果是动态面板，需要从分组中移除
        if (form.IsDynamicForm && !string.IsNullOrEmpty(form.DynamicGroupID))
        {
            if (dynamicFormGroups.TryGetValue(form.DynamicGroupID, out var group))
            {
                group.Remove(form);
            }
        }

        if (forms.ContainsKey(key))
        {
            showForms.Remove(form);

            var tmpStack = new Stack<UIFormBase>();
            while (showFormStack.Count > 0)
            {
                var top = showFormStack.Pop();
                if (top != form) tmpStack.Push(top);
            }
            showFormStack = new Stack<UIFormBase>(tmpStack);

            forms.Remove(key);
        }
    }

    /// <summary>
    /// 手动注册已有面板实例（用于场景中已有面板）
    /// </summary>
    public void RegisterFormInstance(UIFormBase formInstance)
    {
        if (formInstance.IsDynamicForm)
        {
            Debug.LogError($"Cannot register dynamic form instance {formInstance.name} using RegisterFormInstance");
            return;
        }

        string key = formInstance.GetType().Name;
        if (!forms.ContainsKey(key))
        {
            forms.Add(key, formInstance);
            formInstance.Close(); // 默认关闭
        }
    }

    #endregion

    #region 显示与隐藏

    public void ShowUIForm(string className)
    {
        if (!forms.ContainsKey(className))
        {
            CreateForm(className);
            if (!forms.ContainsKey(className)) return;
        }

        var form = forms[className];
        if (form != null && !showForms.Contains(form))
        {
            form.Open(this);
            showForms.Add(form);
            showFormStack.Push(form);
        }
    }

    public void ShowUIForm<T>() where T : UIFormBase => ShowUIForm(typeof(T).Name);

    public void HideUIForm(string className)
    {
        var form = GetForm(className);
        if (form != null && showForms.Contains(form))
        {
            showForms.Remove(form);
            form.Close();
        }
    }

    public void HideUIForm<T>() where T : UIFormBase => HideUIForm(typeof(T).Name);

    public void HideUIFormTurn()
    {
        if (showFormStack.Count > 0)
        {
            var form = showFormStack.Pop();
            HideUIForm(form.name);
        }
    }

    public void HideAllUIForm()
    {
        foreach (var form in showForms)
            form.Close();

        showForms.Clear();
        showFormStack.Clear();
    }

    public bool HasActiveForm() => showForms.Count > 0;

    private void CreateForm(string className)
    {
        if (formPrefabs.TryGetValue(className, out GameObject prefab))
        {
            // 获取UI表单对应的Canvas
            Canvas parentCanvas = GetCanvasForForm(className);

            if (parentCanvas == null)
            {
                Debug.LogWarning($"No canvas mapped for form {className}. Creating at root level.");
            }

            var formObj = GameObject.Instantiate(prefab, parentCanvas?.transform);
            formObj.name = className;
        }
        else
        {
            Debug.LogError($"[UIManager] CreateForm Failed: no prefab registered for class {className}");
        }
    }

    #endregion

    #region 预加载

    public void PreLoadForm(GameObject prefab)
    {
        if (prefab == null) return;

        var formBase = prefab.GetComponent<UIFormBase>();
        if (formBase == null)
        {
            Debug.LogError($"Prefab {prefab.name} does not have UIFormBase component!");
            return;
        }

        string className = formBase.GetType().Name;
        if (!formPrefabs.ContainsKey(className))
        {
            formPrefabs.Add(className, prefab);
        }
    }

    public void PreLoadForms(GameObject[] prefabs)
    {
        foreach (var prefab in prefabs)
        {
            PreLoadForm(prefab);
        }
    }

    #endregion

    #region 快捷访问

    public UIFormBase GetForm(string className) => forms.TryGetValue(className, out var f) ? f : null;

    public T GetForm<T>() where T : UIFormBase => GetForm(typeof(T).Name) as T;

    public bool IsShown(string className)
    {
        var form = GetForm(className);
        return form != null && form.CurrentState == FormState.Opened;
    }

    public UIFormBase TryShowForm(string className, string canvasGroupID = null)
    {
        ShowUIForm(className);
        return GetForm(className);
    }

    #endregion

    #region 动态生成面板扩展

    /// <summary>
    /// 动态面板显示方法
    /// </summary>
    /// <param name="form"></param>
    public void ShowDynamicForm(UIFormBase form)
    {
        if (form != null && !showForms.Contains(form))
        {
            form.Open(this);
            showForms.Add(form);
            // 注意：动态面板不加入堆栈，由分组单独管理
        }
    }

    // 添加动态面板隐藏方法
    public void HideDynamicForm(UIFormBase form)
    {
        if (form != null && showForms.Contains(form))
        {
            showForms.Remove(form);
            form.Close();
            // 动态面板不需要堆栈操作
        }
    }

    /// <summary>
    /// 动态面板创建方法
    /// </summary>
    public T CreateDynamicForm<T>(
        GameObject prefab,
        string groupID,
         Transform parent,
        UIFormConfigSO config = null,
        Action<T> onCreated = null
        ) where T : UIFormBase
    {
        if (prefab == null)
        {
            Debug.LogError("CreateDynamicForm failed: prefab is null");
            return null;
        }

        // 自动从配置获取Canvas
        Canvas targetCanvas = GetCanvasGroup(groupID);
        if (targetCanvas == null)
        {
            Debug.LogError($"Canvas for group '{groupID}' not found!");
            return null;
        }

        Transform spawnParent = parent != null ? parent : targetCanvas?.transform;

        var formObj = UnityEngine.Object.Instantiate(prefab, spawnParent);
        var form = formObj.GetComponent<T>();

        if (form == null)
        {
            UnityEngine.Object.Destroy(formObj);
            Debug.LogError($"Prefab does not contain {typeof(T)} component!");
            return null;
        }

        form.InitializeAsDynamic(groupID, config);
        RegisterDynamicForm(form, groupID);

        // 调用创建后回调
        onCreated?.Invoke(form);

        return form;
    }

    /// <summary>
    /// 获取动态面板组的方法
    /// </summary>
    /// <param name="groupID"></param>
    /// <returns></returns>
    public List<UIFormBase> GetDynamicFormsInGroup(string groupID)
    {
        if (dynamicFormGroups.TryGetValue(groupID, out var forms))
        {
            return forms;
        }
        return new List<UIFormBase>();
    }

    /// <summary>
    /// 获取动态面板组内的面板数量
    /// </summary>
    public int GetDynamicFormCount(string groupID)
    {
        if (dynamicFormGroups.TryGetValue(groupID, out var forms))
        {
            return forms.Count;
        }
        return 0;
    }

    /// <summary>
    /// 清除动态面板组的方法
    /// </summary>
    /// <param name="groupID"></param>
    public void ClearDynamicFormsInGroup(string groupID)
    {
        if (!dynamicFormGroups.ContainsKey(groupID)) return;

        foreach (var form in dynamicFormGroups[groupID].ToArray())
        {
            if (form != null)
            {
                HideDynamicForm(form);
            }
        }

        dynamicFormGroups[groupID].Clear();
    }

    /// <summary>
    /// 设置指定组内所有面板的透明度
    /// </summary>
    /// <param name="groupID">组 ID</param>
    /// <param name="excludePanel">排除的面板</param>
    /// <param name="normalAlpha">正常透明度</param>
    /// <param name="selectedAlpha">选中透明度</param>
    public void SetGroupPanelsAlpha(string groupID, UIFormBase excludePanel, float normalAlpha, float selectedAlpha)
    {
        if (dynamicFormGroups.TryGetValue(groupID, out var forms))
        {
            foreach (var form in forms)
            {
                var canvasGroup = form.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    if (form == excludePanel)
                    {
                        canvasGroup.alpha = selectedAlpha;
                    }
                    else
                    {
                        canvasGroup.alpha = normalAlpha;
                    }
                }
            }
        }
    }

    #endregion
}

public interface IUIForm
{
    void RegisterForm() => UIManager.Instance.RegisterForm(this);
    void UnRegisterForm() => UIManager.Instance.UnRegisterForm(this);
    UIFormBase GetUIFormBase();
}

public enum FormAnimType
{
    None,
    Fade,
    Zoom,
    Pop,
    SlideLeft,
    SlideRight,
    SlideUp,
    SlideDown,
    FadeSlide,

}
