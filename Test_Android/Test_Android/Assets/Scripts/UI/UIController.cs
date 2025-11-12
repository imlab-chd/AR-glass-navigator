using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.Android;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

public class UIController : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI VoicesText;
    public TextMeshProUGUI destPosText;
    public TextMeshProUGUI apiStatusText;
    public TMP_InputField destinationInput;
    public Button confirmButton;
    public Button voicesButton;
    public PlanRouteNavigate navigator;
    public UDPServer udpServer;
    private string defaultDestination = "长安大学渭水校区北门";

    // 语音识别Gee相关
    private bool isLongPressing = false;
    private float pressStartTime;
    private const float longPressThreshold = 0.5f; // 长按阈值0.5秒
    private AudioClip recordedAudioClip;
    private bool isRecording = false;

    void Start()
    {
        if (statusText == null || destPosText == null || apiStatusText == null ||
            destinationInput == null || confirmButton == null || voicesButton == null ||
            navigator == null || udpServer == null)
        {
            Debug.LogError("UI 组件或 UDP 服务未在 Inspector 中赋值！");
            return;
        }

        statusText.text = "初始化中...";
        VoicesText.text = "语音提示: 等待导航开始";
        destPosText.text = $"目的位置: {defaultDestination}";
        apiStatusText.text = "API 状态: 未调用";
        destinationInput.text = defaultDestination;

        navigator.Initialize(
            (message) =>
            {
                UpdateNavigationUI(message);
                udpServer.SendNavigationMessage(message);
            },
            UpdateApiStatus,
            UpdateStatus
        );

        confirmButton.onClick.AddListener(OnConfirmButtonClicked);

        // 添加长按事件监听
        voicesButton.GetComponent<EventTrigger>().triggers.Clear();
        EventTrigger trigger = voicesButton.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = voicesButton.gameObject.AddComponent<EventTrigger>();
        }
        AddEventTrigger(trigger, EventTriggerType.PointerDown, OnPointerDown);
        AddEventTrigger(trigger, EventTriggerType.PointerUp, OnPointerUp);
    }

    // 实现 IPointerDownHandler
    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            isLongPressing = true;
            pressStartTime = Time.time;
        }
    }

    // 实现 IPointerUpHandler
    public void OnPointerUp(PointerEventData eventData)
    {
        isLongPressing = false;
        if (isRecording)
        {
            EndRecord((text, _) =>
            {
                if (!string.IsNullOrEmpty(text))
                {
                    string cleanedText = RemovePunctuation(text);
                    VoicesText.text = $"语音提示: 识别到 '{cleanedText}'，请点击确认开始导航";
                    destinationInput.text = cleanedText;
                }
                else
                {
                    VoicesText.text = "语音提示: 未识别到有效内容";
                    Debug.LogWarning("语音识别结果为空");
                }
            });
        }
    }

    void Update()
    {
        if (isLongPressing && Time.time - pressStartTime >= longPressThreshold)
        {
            StartRecord();
            isLongPressing = false; // 防止重复触发
        }
    }

    private void StartRecord()
    {
        if (Microphone.devices.Length == 0)
        {
            VoicesText.text = "语音提示: 无可用麦克风";
            Debug.LogError("无可用麦克风设备");
            return;
        }

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
            VoicesText.text = "语音提示: 请授予麦克风权限";
            return;
        }
#endif

        try
        {
            recordedAudioClip = Microphone.Start(Microphone.devices[0], false, 40, 16000); // 最长40秒，16kHz
            isRecording = true;
            VoicesText.text = "语音提示: 正在录音，请说出目的地";
            Debug.Log("开始录音");
        }
        catch (Exception ex)
        {
            VoicesText.text = "语音提示: 录音启动失败";
            Debug.LogError($"录音启动失败: {ex.Message}");
            isRecording = false;
        }
    }

    private void EndRecord(Action<string, AudioClip> speechToTextCallback)
    {
        if (!isRecording || recordedAudioClip == null)
        {
            speechToTextCallback?.Invoke(string.Empty, null);
            return;
        }

        Microphone.End(Microphone.devices[0]);
        isRecording = false;
        VoicesText.text = "语音提示: 录音结束，处理中...";

        // 裁剪无声部分
        recordedAudioClip = TrimSilence(recordedAudioClip, 0.01f);
        if (recordedAudioClip == null)
        {
            VoicesText.text = "语音提示: 录音无效（无有效音频）";
            Debug.LogWarning("裁剪后的AudioClip为空");
            speechToTextCallback?.Invoke(string.Empty, null);
            return;
        }

        // 发送语音识别请求
        SendSpeechToTextMsg(recordedAudioClip, text =>
        {
            speechToTextCallback?.Invoke(text, recordedAudioClip);
        });
    }

    private void SendSpeechToTextMsg(AudioClip audioClip, Action<string> callback)
    {
        byte[] bytes = AudioClipToBytes(audioClip);
        JObject jObject = new JObject { ["data"] = Convert.ToBase64String(bytes) };
        StartCoroutine(SendSpeechToTextMsgCoroutine(jObject, callback));
    }

    private IEnumerator SendSpeechToTextMsgCoroutine(JObject message, Action<string> callback)
    {
        Task<string> resultJson = XunFeiManager.Instance.SpeechToText(message);
        yield return new WaitUntil(() => resultJson.IsCompleted);

        if (resultJson.IsCompletedSuccessfully)
        {
            JObject obj = JObject.Parse(resultJson.Result);
            string text = obj["text"]?.ToString();
            Debug.Log($"讯飞语音转文本成功！文本为：{text}");
            callback?.Invoke(text);
        }
        else
        {
            VoicesText.text = "语音提示: 识别失败";
            Debug.LogError($"讯飞语音转文本失败: {resultJson.Exception?.Message}");
            callback?.Invoke(string.Empty);
        }
    }

    private static byte[] AudioClipToBytes(AudioClip audioClip)
    {
        float[] data = new float[audioClip.samples];
        audioClip.GetData(data, 0);
        int rescaleFactor = 32767; // 转换为 16-bit PCM
        byte[] outData = new byte[data.Length * 2];
        for (int i = 0; i < data.Length; i++)
        {
            short temshort = (short)(data[i] * rescaleFactor);
            byte[] temdata = BitConverter.GetBytes(temshort);
            outData[i * 2] = temdata[0];
            outData[i * 2 + 1] = temdata[1];
        }
        return outData;
    }

    private static AudioClip TrimSilence(AudioClip clip, float min)
    {
        var samples = new float[clip.samples];
        clip.GetData(samples, 0);
        return TrimSilence(new List<float>(samples), min, clip.channels, clip.frequency);
    }

    private static AudioClip TrimSilence(List<float> samples, float min, int channels, int hz, bool _3D = false)
    {
        int origSamples = samples.Count;

        int i;
        for (i = 0; i < samples.Count; i++)
        {
            if (Mathf.Abs(samples[i]) > min)
            {
                break;
            }
        }
        i -= (int)(hz * 0.1f);
        i = Mathf.Max(i, 0);
        samples.RemoveRange(0, i);

        for (i = samples.Count - 1; i > 0; i--)
        {
            if (Mathf.Abs(samples[i]) > min)
            {
                break;
            }
        }
        i += (int)(hz * 0.1f);
        i = Mathf.Min(i, samples.Count - 1);
        samples.RemoveRange(i, samples.Count - i);

        if (samples.Count == 0)
        {
            Debug.LogWarning("裁剪后的AudioClip长度为0");
            return null;
        }

        var clip = AudioClip.Create("TempClip", samples.Count, channels, hz, _3D);
        clip.SetData(samples.ToArray(), 0);
        return clip;
    }

    private string RemovePunctuation(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"[。！？，,.!?]", "");
    }

    private void OnConfirmButtonClicked()
    {
        string input = destinationInput.text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            statusText.text = "错误：请输入有效目的地";
            return;
        }
        navigator.StartNavigation(input, (destination) =>
        {
            destPosText.text = $"目的位置: {destination}";
        });
    }

    private void UpdateNavigationUI(NavigationMessage message)
    {
        statusText.text = $"转向: {message.TurnInstruction}\n" +
                          $"距离下一个路口: {message.DistanceText}\n" +
                          $"当前道路: {message.CurrentRoad}\n" +
                          $"下一道路: {message.NextRoad}";
    }

    private void UpdateApiStatus(string status)
    {
        apiStatusText.text = status;
    }

    private void UpdateStatus(string status)
    {
        statusText.text = status;
    }

    void OnDestroy()
    {
        if (navigator != null)
        {
            navigator.StopNavigation();
        }
        if (isRecording)
        {
            Microphone.End(Microphone.devices[0]);
            isRecording = false;
        }
    }

    private void AddEventTrigger(EventTrigger trigger, EventTriggerType eventType, System.Action<PointerEventData> callback)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = eventType };
        entry.callback.AddListener((eventData) => callback((PointerEventData)eventData));
        trigger.triggers.Add(entry);
    }
}