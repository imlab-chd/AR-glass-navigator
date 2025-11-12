// using UnityEngine;
// using System.Net.Sockets;
// using System.Text;
// using System;
// using Crosstales.RTVoice.Tool;
// using Crosstales.RTVoice;

// public class UDPServer : MonoBehaviour
// {
//     private UdpClient udpClient;
//     //private string clientIP = "255.255.255.255"; // 广播地址
//     public string clientIP = "10.81.196.221";
//     private int clientPort = 12345; // 客户端监听端口
//     private NavigationMessage latestMessage;
//     public SpeechText SpeechText;


//     void Start()
//     {
//         udpClient = new UdpClient();
//         //udpClient.EnableBroadcast = true;
//     }

//     public void SendNavigationMessage(NavigationMessage message)
//     {
//         // if (latestMessage != null && JsonUtility.ToJson(latestMessage) == JsonUtility.ToJson(message)) return;

//         latestMessage = message;
//         try
//         {
//             string json = JsonUtility.ToJson(message);
//             byte[] data = Encoding.UTF8.GetBytes(json);
//             udpClient.Send(data, data.Length, clientIP, clientPort);
//             Debug.Log($"UDPServer 发送 JSON: {json}, 目标: {clientIP}:{clientPort}, 长度: {data.Length}");
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"UDP 发送失败: {e.Message}");
//         }

//         OnSpeech(message);
//     }

//     private string FormatSpeechText(NavigationMessage message)
//     {
//         // 处理空值或无效数据
//         string turn = string.IsNullOrEmpty(message.TurnInstruction) ? "继续前行" : message.TurnInstruction;
//         string distance = string.IsNullOrEmpty(message.DistanceText) ? "未知距离" : message.DistanceText;
//         string currentRoad = string.IsNullOrEmpty(message.CurrentRoad) ? "当前道路" : message.CurrentRoad;
//         string nextRoad = string.IsNullOrEmpty(message.NextRoad) ? "" : message.NextRoad;

//         // 提取距离数值（假设格式为“100米”或“1.5公里”）
//         double distanceValue = 0;
//         bool isDistanceValid = false;
//         if (!string.IsNullOrEmpty(message.DistanceText))
//         {
//             string numericPart = System.Text.RegularExpressions.Regex.Match(message.DistanceText, @"[\d\.]+").Value;
//             if (double.TryParse(numericPart, out distanceValue))
//             {
//                 isDistanceValid = true;
//                 if (message.DistanceText.Contains("公里"))
//                 {
//                     distanceValue *= 1000; // 转换为米
//                 }
//             }
//         }

//         // 根据距离调整提示语
//         string distancePrompt = isDistanceValid ? (distanceValue <= 50 ? "" : (distanceValue >= 1000 ? "大约" : "前方")) : "前方";

//         // 特殊转向指令处理
//         string turnPrompt;
//         switch (turn)
//         {
//             case "掉头":
//                 turnPrompt = "请掉头";
//                 break;
//             case "进入环岛":
//                 turnPrompt = "请进入环岛";
//                 break;
//             case "直行":
//                 turnPrompt = "请继续直行";
//                 break;
//             case "左转":
//             case "右转":
//             case "靠左":
//             case "靠右":
//             case "斜左":
//             case "斜右":
//             case "进入主路":
//             case "进入辅路":
//                 turnPrompt = $"请{turn}";
//                 break;
//             default:
//                 turnPrompt = "请继续前行";
//                 break;
//         }

//         // 构造播报内容
//         string speech = $"{turnPrompt}";
//         if (isDistanceValid)
//         {
//             speech += $"，{distancePrompt}{distance}后";
//         }
//         else
//         {
//             speech += "，前方";
//         }

//         // 处理道路信息
//         if (!string.IsNullOrEmpty(nextRoad) && nextRoad != currentRoad)
//         {
//             speech += $"，进入{nextRoad}";
//         }
//         else if (turn != "掉头" && turn != "进入环岛") // 掉头和环岛不重复提当前道路
//         {
//             speech += $"，沿{currentRoad}继续行驶";
//         }

//         return speech;
//     }

//     private void OnSpeech(NavigationMessage message)
//     {
//         if (SpeechText == null)
//         {
//             Debug.LogWarning("SpeechText 组件未设置，无法播报");
//             return;
//         }

//         string speech = FormatSpeechText(message);
//         if (string.IsNullOrEmpty(speech))
//         {
//             Debug.LogWarning("生成的播报内容为空，无法播报");
//             return;
//         }

//         try
//         {
//             SpeechText.Text = speech;
//             SpeechText.Speak();
//             Debug.Log($"UDPServer 播报: {speech}");
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"UDPServer 播报失败: {e.Message}");
//         }
//     }

//     void OnDestroy()
//     {
//         udpClient?.Close();
//     }
// }

using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

[RequireComponent(typeof(AudioSource))]
public class UDPServer : MonoBehaviour
{
    private UdpClient udpClient;
    public string clientIP = "10.81.196.221";
    private int clientPort = 12345; // 客户端监听端口
    private NavigationMessage latestMessage;
    private AudioSource audioSource;

    void Start()
    {
        udpClient = new UdpClient();
        audioSource = GetComponent<AudioSource>();
    }

    public void SendNavigationMessage(NavigationMessage message)
    {
        latestMessage = message;
        try
        {
            string json = JsonUtility.ToJson(message);
            byte[] data = Encoding.UTF8.GetBytes(json);
            udpClient.Send(data, data.Length, clientIP, clientPort);
            Debug.Log($"UDPServer 发送 JSON: {json}, 目标: {clientIP}:{clientPort}, 长度: {data.Length}");
        }
        catch (Exception e)
        {
            Debug.LogError($"UDP 发送失败: {e.Message}");
        }

        OnSpeech(message);
    }

    private string FormatSpeechText(NavigationMessage message)
    {
        // 处理空值或无效数据
        string turn = string.IsNullOrEmpty(message.TurnInstruction) ? "继续前行" : message.TurnInstruction;
        string distance = string.IsNullOrEmpty(message.DistanceText) ? "未知距离" : message.DistanceText;
        string currentRoad = string.IsNullOrEmpty(message.CurrentRoad) ? "当前道路" : message.CurrentRoad;
        string nextRoad = string.IsNullOrEmpty(message.NextRoad) ? "" : message.NextRoad;

        // 提取距离数值（假设格式为“100米”或“1.5公里”）
        double distanceValue = 0;
        bool isDistanceValid = false;
        if (!string.IsNullOrEmpty(message.DistanceText))
        {
            string numericPart = System.Text.RegularExpressions.Regex.Match(message.DistanceText, @"[\d\.]+").Value;
            if (double.TryParse(numericPart, out distanceValue))
            {
                isDistanceValid = true;
                if (message.DistanceText.Contains("公里"))
                {
                    distanceValue *= 1000; // 转换为米
                }
            }
        }

        // 根据距离调整提示语
        string distancePrompt = isDistanceValid ? (distanceValue <= 50 ? "" : (distanceValue >= 1000 ? "大约" : "前方")) : "前方";

        // 特殊转向指令处理
        string turnPrompt;
        switch (turn)
        {
            case "掉头":
                turnPrompt = "请掉头";
                break;
            case "进入环岛":
                turnPrompt = "请进入环岛";
                break;
            case "直行":
                turnPrompt = "请继续直行";
                break;
            case "左转":
            case "右转":
            case "靠左":
            case "靠右":
            case "斜左":
            case "斜右":
            case "进入主路":
            case "进入辅路":
                turnPrompt = $"请{turn}";
                break;
            default:
                turnPrompt = "请继续前行";
                break;
        }

        // 构造播报内容
        string speech = $"{turnPrompt}";
        if (isDistanceValid)
        {
            speech += $"，{distancePrompt}{distance}后";
        }
        else
        {
            speech += "，前方";
        }

        // 处理道路信息
        if (!string.IsNullOrEmpty(nextRoad) && nextRoad != currentRoad)
        {
            speech += $"，进入{nextRoad}";
        }
        else if (turn != "掉头" && turn != "进入环岛") // 掉头和环岛不重复提当前道路
        {
            speech += $"，沿{currentRoad}继续行驶";
        }

        return speech;
    }

    private void OnSpeech(NavigationMessage message)
    {
        string speech = FormatSpeechText(message);
        if (string.IsNullOrEmpty(speech))
        {
            Debug.LogWarning("生成的播报内容为空，无法播报");
            return;
        }

        // 调用讯飞 TTS
        SendTextToSpeechMsg(speech, audioClip =>
        {
            if (audioClip != null)
            {
                audioSource.clip = audioClip;
                audioSource.Play();
                Debug.Log($"UDPServer 播报: {speech}");
            }
            else
            {
                Debug.LogError("讯飞文本转语音失败，音频为空");
            }
        });
    }

    private void SendTextToSpeechMsg(string text, Action<AudioClip> callback)
    {
        JObject jObject = new JObject
        {
            ["text"] = text,
            ["voice"] = "xiaoyan" // 使用小燕音色，可在讯飞控制台调整
        };
        StartCoroutine(SendTextToSpeechMsgCoroutine(jObject, callback));
    }

    private IEnumerator SendTextToSpeechMsgCoroutine(JObject message, Action<AudioClip> callback)
    {
        Task<string> resultJson = XunFeiManager.Instance.TextToSpeech(message);
        yield return new WaitUntil(() => resultJson.IsCompleted);

        if (resultJson.IsCompletedSuccessfully)
        {
            try
            {
                JObject obj = JObject.Parse(resultJson.Result);
                string base64Audio = obj["data"]?.ToString();
                if (string.IsNullOrEmpty(base64Audio))
                {
                    Debug.LogError("讯飞文本转语音失败，音频数据为空");
                    callback?.Invoke(null);
                    yield break;
                }

                float[] audioData = BytesToFloat(Convert.FromBase64String(base64Audio));
                if (audioData.Length == 0)
                {
                    Debug.LogError("讯飞文本转语音失败，音频数据长度为0");
                    callback?.Invoke(null);
                    yield break;
                }

                AudioClip audioClip = AudioClip.Create("SynthesizedAudio", audioData.Length, 1, 16000, false);
                audioClip.SetData(audioData, 0);
                callback?.Invoke(audioClip);
            }
            catch (Exception e)
            {
                Debug.LogError($"讯飞文本转语音解析失败: {e.Message}");
                callback?.Invoke(null);
            }
        }
        else
        {
            Debug.LogError($"讯飞文本转语音请求失败: {resultJson.Exception?.Message}");
            callback?.Invoke(null);
        }
    }

    private static float[] BytesToFloat(byte[] byteArray)
    {
        float[] sounddata = new float[byteArray.Length / 2];
        for (int i = 0; i < sounddata.Length; i++)
        {
            short s = BitConverter.IsLittleEndian
                ? (short)((byteArray[i * 2 + 1] << 8) | byteArray[i * 2])
                : (short)((byteArray[i * 2] << 8) | byteArray[i * 2 + 1]);
            sounddata[i] = s / 32768.0f;
        }
        return sounddata;
    }

    void OnDestroy()
    {
        udpClient?.Close();
    }
}
