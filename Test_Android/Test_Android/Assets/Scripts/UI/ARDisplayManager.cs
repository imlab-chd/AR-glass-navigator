using UnityEngine;
using TMPro;

public class ARDisplayManager : MonoBehaviour
{
    public Camera mainCamera; // AR 眼镜主摄像机
    public GameObject[] prefabIcons; // 箭头预制体数组（对应 IconID 0-10）
    public GameObject text3DPrefab; // 3D 文本预制体
    public float displayDistance = 5f; // 显示距离（摄像机前方，适合驾车）
    public float sideOffset = 2f; // 横向偏移（道路左侧/右侧）
    public float textOffsetY = 0.5f; // 距离文本的 Y 轴偏移（箭头上方）
    public float textSideOffset = 0.5f; // 道路文本的横向偏移（环绕箭头）
    public float iconScale = 0.3f; // 箭头统一缩放比例
    public float textAnimationSpeed = 2f; // 文本动画速度
    public float textAnimationAmplitude = 0.05f; // 动画幅度
    public float textFontSize = 2f; // 文本字体大小

    private GameObject currentIcon; // 当前箭头实例
    private GameObject distanceText; // 距离文本实例
    private GameObject roadText; // 道路文本实例
    private int IconID;
    private float animationTime;

    void Start()
    {
        // 验证配置
        if (mainCamera == null)
        {
            Debug.LogError("ARDisplayManager: mainCamera 未赋值！");
            return;
        }
        if (prefabIcons == null || prefabIcons.Length < 11)
        {
            Debug.LogError("ARDisplayManager: prefabIcons 数组未配置或长度不足 11！");
            return;
        }
        if (text3DPrefab == null)
        {
            Debug.LogError("ARDisplayManager: text3DPrefab 未赋值！");
            return;
        }
        if (text3DPrefab.GetComponent<TextMeshPro>() == null)
        {
            Debug.LogError("ARDisplayManager: text3DPrefab 缺少 TextMeshPro 组件！");
            return;
        }

        // 初始化
        UDPClient udpClient = GetComponent<UDPClient>();
        if (udpClient != null)
        {
            udpClient.OnMessageReceived += UpdateDisplay;
            Debug.Log("ARDisplayManager 已绑定 UDPClient");
        }
        else
        {
            Debug.LogError("ARDisplayManager: 未找到 UDPClient 组件！");
        }
    }

    private void UpdateDisplay(NavigationMessage message)
    {
        // 验证消息
        if (message == null || message.IconID < 0 || message.IconID >= prefabIcons.Length)
        {
            Debug.LogWarning($"无效的 NavigationMessage: message={message}, IconID={message?.IconID}");
            return;
        }

        // 销毁旧对象
        if (currentIcon != null)
        {
            Destroy(currentIcon);
        }
        if (distanceText != null)
        {
            Destroy(distanceText);
        }
        if (roadText != null)
        {
            Destroy(roadText);
        }

        // 计算箭头位置（道路两侧）
        Vector3 forward = mainCamera.transform.forward * displayDistance;
        float offsetX = GetSideOffset(message.IconID);
        Vector3 side = mainCamera.transform.right * offsetX;
        Vector3 spawnPosition = mainCamera.transform.position + forward + side;

        // 实例化新箭头
        IconID = message.IconID;
        currentIcon = Instantiate(prefabIcons[IconID], spawnPosition, Quaternion.identity);
        currentIcon.transform.localScale = Vector3.one * iconScale;
        currentIcon.transform.LookAt(mainCamera.transform.position);

        // 实例化距离文本（箭头上方）
        Vector3 distanceTextPosition = spawnPosition + new Vector3(0, textOffsetY, 0);
        distanceText = Instantiate(text3DPrefab, distanceTextPosition, Quaternion.identity);
        TextMeshPro distanceMesh = distanceText.GetComponent<TextMeshPro>();
        distanceMesh.text = $"剩余距离: {(string.IsNullOrEmpty(message.DistanceText) ? "未知" : message.DistanceText)}";
        distanceMesh.alignment = TextAlignmentOptions.Center;
        distanceMesh.fontSize = textFontSize;
        distanceText.transform.LookAt(mainCamera.transform.position);
        distanceText.transform.Rotate(0, 180, 0);

        // 实例化道路文本（箭头侧面，动态偏移）
        float roadSideOffset = (IconID == 1 || IconID == 3 || IconID == 5) ? -textSideOffset : textSideOffset;
        Vector3 roadTextPosition = spawnPosition + new Vector3(roadSideOffset, textOffsetY * 0.5f, 0);
        roadText = Instantiate(text3DPrefab, roadTextPosition, Quaternion.identity);
        TextMeshPro roadMesh = roadText.GetComponent<TextMeshPro>();
        roadMesh.text = string.IsNullOrEmpty(message.CurrentRoad) ? "未知" : message.CurrentRoad;
        roadMesh.alignment = TextAlignmentOptions.Center;
        roadMesh.fontSize = textFontSize * 0.8f;
        roadText.transform.LookAt(mainCamera.transform.position);
        roadText.transform.Rotate(0, 180, 0);

        animationTime = 0f;
    }

    void Update()
    {
        if (currentIcon == null || distanceText == null || roadText == null)
            return;

        // 更新位置（跟随摄像机，保持道路两侧）
        Vector3 forward = mainCamera.transform.forward * displayDistance;
        float offsetX = GetSideOffset(IconID);
        Vector3 side = mainCamera.transform.right * offsetX;
        Vector3 targetPosition = mainCamera.transform.position + forward + side;

        // 箭头位置
        currentIcon.transform.position = targetPosition;
        currentIcon.transform.LookAt(mainCamera.transform.position);

        // 距离文本位置（上方，带浮动动画）
        Vector3 distanceTextPosition = targetPosition + new Vector3(0, textOffsetY, 0);
        animationTime += Time.deltaTime * textAnimationSpeed;
        float floatOffset = textAnimationAmplitude * Mathf.Sin(animationTime);
        distanceText.transform.position = distanceTextPosition + new Vector3(0, floatOffset, 0);
        distanceText.transform.LookAt(mainCamera.transform.position);
        distanceText.transform.Rotate(0, 180, 0);

        // 道路文本位置（侧面，带摆动和缩放动画）
        float roadSideOffset = (IconID == 1 || IconID == 3 || IconID == 5) ? -textSideOffset : textSideOffset;
        Vector3 roadTextPosition = targetPosition + new Vector3(roadSideOffset, textOffsetY * 0.5f, 0);
        float swingOffset = textAnimationAmplitude * Mathf.Sin(animationTime * 0.8f); // 左右摆动
        float scaleFactor = 1f + 0.1f * Mathf.Sin(animationTime * 0.5f); // 缩放脉动
        roadText.transform.position = roadTextPosition + new Vector3(swingOffset, 0, 0);
        roadText.transform.localScale = Vector3.one * scaleFactor;
        roadText.transform.LookAt(mainCamera.transform.position);
        roadText.transform.Rotate(0, 180 + 10f * Mathf.Sin(animationTime), 0); // 增强旋转
    }

    private float GetSideOffset(int iconID)
    {
        switch (iconID)
        {
            case 1: // 左转
            case 3: // 左前
            case 5: // 左后
                return -sideOffset;
            case 2: // 右转
            case 4: // 右前
            case 6: // 右后
                return sideOffset;
            default: // 直行、到达等
                return sideOffset * 0.5f;
        }
    }

    void OnDestroy()
    {
        UDPClient udpClient = GetComponent<UDPClient>();
        if (udpClient != null)
        {
            udpClient.OnMessageReceived -= UpdateDisplay;
        }
    }
}