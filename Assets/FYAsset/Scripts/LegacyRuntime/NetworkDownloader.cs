using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 对接 Netlify / CDN / 任意服务器
/// 下载版本文件、catalog、bundle 等文件
/// </summary>
public class NetworkDownloader : Singleton<NetworkDownloader>
{
    public event Action<float> OnProgress; 
    
    // 配置重试参数
    private const int MAX_RETRIES = 3;
    private const int RETRY_INTERVAL_MS = 1000; // 1秒
    
    /// <summary>
    /// 下载文件
    /// </summary>
    public async Task<bool> DownloadFile(string url, string savePath)
    {
        for (int i = 0; i <= MAX_RETRIES; i++)
        {
            if (i > 0)
            {
                Debug.LogWarning($"[NetworkDownloader] 开始第 {i} 次重试下载: {url}");
                if (File.Exists(savePath)) File.Delete(savePath); 
            }
            
            using var uwr = UnityWebRequest.Get(url);

            uwr.downloadHandler = new DownloadHandlerFile(savePath) {removeFileOnAbort = true};
            var operation = uwr.SendWebRequest();

            while (!operation.isDone)
            {
                OnProgress?.Invoke(operation.progress);
                await Task.Yield();
            }

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[NetworkDownloader] 下载文件成功: {url}");
                return true;
            }

            if (uwr.responseCode == 404)
            {
                Debug.LogError($"[NetworkDownloader] 文件未找到 (404)，停止重试: {url}");
                return false;
            }
            
            if (i == MAX_RETRIES)
            {
                Debug.LogError($"[NetworkDownloader] 下载文件失败 (已重试{MAX_RETRIES}次): {url}\n错误: {uwr.error}");
                return false;
            }

            // 等待一段时间后重试
            await Task.Delay(RETRY_INTERVAL_MS);
        }
        return false;
    }

    /// <summary>
    /// 下载文本
    /// </summary>
    public async Task<string> DownloadText(string url)
    {
        for (int i = 0; i <= MAX_RETRIES; i++)
        {
            if(i > 0) Debug.LogWarning($"[NetworkDownloader] 开始第 {i} 次重试获取文本: {url}");
            
            using var uwr = UnityWebRequest.Get(url);
            var operation = uwr.SendWebRequest();

            while (!operation.isDone)
            {
                OnProgress?.Invoke(operation.progress);
                await Task.Yield();
            }

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                return uwr.downloadHandler.text;
            }

            if (uwr.responseCode == 404)
            {
                Debug.LogError($"[NetworkDownloader] 文本未找到 (404): {url}");
                return null;
            }

            if (i == MAX_RETRIES)
            {
                Debug.LogError($"[NetworkDownloader] 下载文本失败: {url}\n{uwr.error}");
                return null;
            }

            await Task.Delay(RETRY_INTERVAL_MS);
        }
        return null;
    }

    /// <summary>
    /// 下载字节
    /// </summary>
    public async Task<byte[]> DownloadBytes(string url)
    {
        for (int i = 0; i <= MAX_RETRIES; i++)
        {
            if (i > 0) Debug.LogWarning($"[NetworkDownloader] 开始第 {i} 次重试获取字节: {url}");
            
            using var uwr = UnityWebRequest.Get(url);
            var operation = uwr.SendWebRequest();

            while (!operation.isDone)
            {
                OnProgress?.Invoke(operation.progress);
                await Task.Yield();
            }

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                return uwr.downloadHandler.data;
            }

            if (uwr.responseCode == 404)
            {
                Debug.LogError($"[NetworkDownloader] 资源未找到 (404): {url}");
                return null;
            }

            if (i == MAX_RETRIES)
            {
                Debug.LogError($"[NetworkDownloader] 下载字节失败: {url}\n{uwr.error}");
                return null;
            }

            await Task.Delay(RETRY_INTERVAL_MS);
        }
        return null;
    }
}
