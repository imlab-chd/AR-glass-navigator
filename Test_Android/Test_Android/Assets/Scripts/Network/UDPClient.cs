using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System;

public class UDPClient : MonoBehaviour
{
    private UdpClient udpClient;
    private int listenPort = 12345; // 与服务端一致
    private Thread receiveThread;
    private bool isRunning = true;
    public Action<NavigationMessage> OnMessageReceived;

    void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            udpClient = new UdpClient(listenPort);
            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            Debug.Log("UDPClient 初始化完成，监听端口: " + listenPort);
        }
        catch (Exception e)
        {
            Debug.LogError($"UDPClient 初始化失败: {e.Message}");
        }
    }

    private void ReceiveData()
    {
        while (isRunning)
        {
            try
            {
                var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref endpoint);
                string json = Encoding.UTF8.GetString(data);
                Debug.Log($"UDPClient 接收 JSON: {json}, 来源: {endpoint.Address}:{endpoint.Port}");
                NavigationMessage message = JsonUtility.FromJson<NavigationMessage>(json);
                // 调度到主线程
                UnityMainThreadDispatcher.Instance().Enqueue(() => OnMessageReceived?.Invoke(message));
            }
            catch (Exception e)
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    Debug.LogError($"UDP 接收失败: {e.Message}")
                );
            }
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        receiveThread?.Abort();
        udpClient?.Close();
    }
}