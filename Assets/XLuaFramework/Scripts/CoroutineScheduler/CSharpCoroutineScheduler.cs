using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XLua;
using XLua.LuaDLL;

public static class CSharpCoroutineScheduler
{
    private static MonoBehaviour _runner;
    private static readonly Dictionary<int, Coroutine> _idToCoroutine = new();
    private static readonly Dictionary<Coroutine, int> _coroutineToId = new();
    private static int _idCounter;
    
    // 当前协程ID栈（支持嵌套协程）
    private static readonly Stack<int> _currentCoroutineStack = new();
    
    public static void Init(MonoBehaviour runner) => _runner = runner;
    
    /// <summary>
    /// 启动协程返回ID
    /// </summary>
    public static int StartCoroutine(IEnumerator routine)
    {
        if (_runner == null) 
            throw new System.Exception("CSharpCoroutineScheduler not initialized!");
        if (routine == null) 
            throw new ArgumentNullException("routine");
        
        var id = ++_idCounter;
        var coroutine = _runner.StartCoroutine(
            WrappedCoroutine(id, routine)
        );
        
        _idToCoroutine[id] = coroutine;
        _coroutineToId[coroutine] = id;
        return id;
    }
    
    private static IEnumerator WrappedCoroutine(int id, IEnumerator routine)
    {
        // 压入当前协程ID
        _currentCoroutineStack.Push(id);
        
        try
        {
            yield return routine;
        }
        finally
        {
            // 确保清理资源
            CoroutineBridge.NotifyCSharpComplete(id);
            _idToCoroutine.Remove(id);
            
            // 移除当前协程ID
            _currentCoroutineStack.Pop();
        }
    }
    
    /// <summary>
    /// 停止指定协程
    /// </summary>
    public static void StopCoroutine(int id)
    {
        if (!_idToCoroutine.ContainsKey(id)) {
            Debug.LogWarning($"Attempt to stop invalid C# Coroutine ID: {id}");
            return;
        }
        
        if (_idToCoroutine.TryGetValue(id, out var coroutine))
        {
            _runner.StopCoroutine(coroutine);
            _idToCoroutine.Remove(id);
            
            if (_coroutineToId.ContainsKey(coroutine))
            {
                _coroutineToId.Remove(coroutine);
            }
            
            CoroutineBridge.NotifyCSharpComplete(id);
        }
    }
    
    /// <summary>
    /// 获取当前协程ID（支持嵌套协程）
    /// </summary>
    public static int GetCurrentCoroutineId()
    {
        return _currentCoroutineStack.Count > 0 
            ? _currentCoroutineStack.Peek() 
            : -1;
    }
    
    /// <summary>
    /// 停止所有C#协程
    /// </summary>
    public static void StopAllCoroutines()
    {
        if (_runner == null) return;
    
        // 停止所有管理的协程
        foreach (var coroutine in _idToCoroutine.Values.ToList())
        {
            _runner.StopCoroutine(coroutine);
        }
    
        // 清空数据结构
        _idToCoroutine.Clear();
        _coroutineToId.Clear();
        _currentCoroutineStack.Clear();
        _idCounter = 0; // 可选重置ID计数器
    }
}