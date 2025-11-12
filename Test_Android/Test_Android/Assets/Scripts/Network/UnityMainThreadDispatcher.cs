using UnityEngine;
using System.Collections.Concurrent;
using System;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher instance;
    private ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();
    private bool isInitialized = false;

    public static UnityMainThreadDispatcher Instance()
    {
        if (instance == null && !Application.isPlaying)
        {
            return null;
        }

        if (instance == null)
        {
            // 在主线程中查找或创建实例
            instance = FindObjectOfType<UnityMainThreadDispatcher>();
            if (instance == null)
            {
                GameObject go = new GameObject("UnityMainThreadDispatcher");
                instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
            instance.isInitialized = true;
        }
        return instance;
    }

    void Awake()
    {
        // 确保单例初始化
        if (instance == null)
        {
            instance = this;
            isInitialized = true;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        if (!isInitialized) return;
        while (actions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }
    }

    public void Enqueue(Action action)
    {
        if (isInitialized)
        {
            actions.Enqueue(action);
        }
        else
        {
            Debug.LogWarning("UnityMainThreadDispatcher 未初始化，忽略 Enqueue 操作");
        }
    }
}