using UnityEngine;
using UnityEngine.Android;
using System.Collections;
using Utils;
using System.Text.RegularExpressions;

public class PlanRouteNavigate : MonoBehaviour
{
    private LocationService locationService;
    private Vector2d currentPosition;
    private string destination; // 目的地址坐标（BD-09LL，格式为 lat,lon）
    private int currentRoadIndex = 0;
    [SerializeField] private double targetChangeRange = 5.0f; // 目标切换范围（米）
    private double gpsUpdateInterval = 0.2f; // GPS 更新间隔（5Hz）
    private Vector2d referencePosition;
    private double lastReplanTime;
    [SerializeField] private double replanCooldown = 5f; // 重新规划冷却时间（秒）
    private bool isNavigating = false;
    private bool isFirstStep = true; // 标记首次路径点
    private NavigationMessage naviMessage = new NavigationMessage();
    [SerializeField] private string startAddress = "西安钟楼"; // 在编辑器中输入起始地址
    [SerializeField] private string destinationAddress = "西安大雁塔"; // 在编辑器中输入目的地址（仅用于 UI 显示）

    private System.Action<NavigationMessage> onNavigationUpdate;
    private System.Action<string> onApiStatusUpdate;
    private System.Action<string> onStatusUpdate;

    public void Initialize(
        System.Action<NavigationMessage> navigationCallback,
        System.Action<string> apiStatusCallback,
        System.Action<string> statusCallback)
    {
        onNavigationUpdate = navigationCallback;
        onApiStatusUpdate = apiStatusCallback;
        onStatusUpdate = statusCallback;

        StartCoroutine(StartLocationService());
    }

    public void StartNavigation(string input, System.Action<string> onDestinationSet)
    {
        onStatusUpdate?.Invoke("正在解析起始地址...");
        onApiStatusUpdate?.Invoke("正在调用地址编码 API...");

        // 获取起始地址的坐标（BD-09LL）
        StartCoroutine(BaiduMapService.Instance.GetCoordinatesFromAddress(startAddress,
            (startCoords) =>
            {
                onApiStatusUpdate?.Invoke("起始地址解析成功");
                onStatusUpdate?.Invoke("正在解析目的地址...");

                // 解析起始坐标为 Vector2d
                string[] startParts = startCoords.Split(',');
                if (startParts.Length != 2 || !double.TryParse(startParts[0], out double startLat) || !double.TryParse(startParts[1], out double startLon))
                {
                    onApiStatusUpdate?.Invoke("起始地址坐标格式无效");
                    onStatusUpdate?.Invoke("错误：起始地址坐标格式无效");
                    return;
                }
                Vector2d start = new Vector2d(startLat, startLon);

                // 处理输入的目的地
                if (Utilities.ValidateDestinationInput(input))
                {
                    // 输入是坐标格式（lat,lon）
                    destination = input;
                    onDestinationSet?.Invoke(destination);
                    onApiStatusUpdate?.Invoke("目的地址坐标有效");
                    StartNavigationInternal(start);
                }
                else
                {
                    // 输入是地址，需解析
                    StartCoroutine(BaiduMapService.Instance.GetCoordinatesFromAddress(input,
                        (destCoords) =>
                        {
                            destination = destCoords;
                            onDestinationSet?.Invoke(destination);
                            onApiStatusUpdate?.Invoke("目的地址解析成功");
                            StartNavigationInternal(start);
                        },
                        (error) =>
                        {
                            onApiStatusUpdate?.Invoke(error);
                            onStatusUpdate?.Invoke($"错误：{error}");
                        }));
                }
            },
            (error) =>
            {
                onApiStatusUpdate?.Invoke(error);
                onStatusUpdate?.Invoke($"错误：{error}");
            }));
    }

    public void StopNavigation()
    {
        isNavigating = false;
        if (locationService != null) locationService.Stop();
    }

    private IEnumerator StartLocationService()
    {
        locationService = Input.location;

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                yield return null;
            }
        }
#endif

        if (!locationService.isEnabledByUser)
        {
            onStatusUpdate?.Invoke("错误：GPS 未启用");
            yield break;
        }

        locationService.Start(1f, 0.5f);
        int maxWait = 10;
        while (locationService.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait <= 0 || locationService.status == LocationServiceStatus.Failed)
        {
            onStatusUpdate?.Invoke("错误：GPS 初始化失败");
            yield break;
        }

        currentPosition = new Vector2d(locationService.lastData.latitude, locationService.lastData.longitude);
        referencePosition = currentPosition;
        onStatusUpdate?.Invoke("GPS 已就绪，请输入目的地并点击确认");
        StartCoroutine(UpdateGPSData());
    }

    private IEnumerator UpdateGPSData()
    {
        while (true)
        {
            if (locationService.status == LocationServiceStatus.Running)
            {
                currentPosition = new Vector2d(locationService.lastData.latitude, locationService.lastData.longitude);
            }
            yield return new WaitForSeconds((float)gpsUpdateInterval);
        }
    }

    private void StartNavigationInternal(Vector2d start)
    {
        if (locationService != null && locationService.status == LocationServiceStatus.Running)
        {
            onStatusUpdate?.Invoke("正在获取导航路径...");
            onApiStatusUpdate?.Invoke("API 调用中...");
            StartCoroutine(BaiduMapService.Instance.GetNavigationPath(start, destination,
                () =>
                {
                    isNavigating = true;
                    isFirstStep = true;
                    currentRoadIndex = 0;
                    lastReplanTime = Time.time;
                    onApiStatusUpdate?.Invoke($"API 调用完成，路径点数: {BaiduMapService.Instance.RoutePoints.Count}");
                    UpdateNavigationInfo(currentRoadIndex);
                },
                (error) =>
                {
                    onApiStatusUpdate?.Invoke(error);
                    onStatusUpdate?.Invoke($"错误：{error}");
                }));
        }
        else
        {
            onStatusUpdate?.Invoke("等待 GPS 初始化完成...");
        }
    }

    void Update()
    {
        if (!BaiduMapService.Instance.IsMapReady || !isNavigating)
            return;

        // 检查是否走完所有路径点
        if (currentRoadIndex >= BaiduMapService.Instance.RoutePoints.Count)
        {
            isNavigating = false;
            onStatusUpdate?.Invoke("导航完成");
            Debug.Log("导航结束：已走完所有路径点");
            return;
        }

        // 将当前 GPS 位置（WGS-84）转换为 BD-09LL
        Vector2d currentPositionBD = Utilities.WGS84ToBD09(currentPosition.x, currentPosition.y);

        // 计算到当前目标点的距离（米）
        double distanceToTarget = Utilities.CalculateLongitudeLatitudeDistance(
            currentPositionBD,
            BaiduMapService.Instance.RoutePoints[currentRoadIndex].coordinate
        ) * 100000; // 转换为米

        // 调试日志
        Debug.Log($"当前 GPS (BD-09LL): ({currentPositionBD.x}, {currentPositionBD.y}), 目标点: ({BaiduMapService.Instance.RoutePoints[currentRoadIndex].coordinate.x}, {BaiduMapService.Instance.RoutePoints[currentRoadIndex].coordinate.y}), 距离: {distanceToTarget:F2} 米");

        // 首次导航时更新导航信息
        if (isFirstStep)
        {
            UpdateNavigationInfo(currentRoadIndex);
            isFirstStep = false;
        }
        // 检测是否到达当前目标点
        else if (distanceToTarget < targetChangeRange && currentRoadIndex < BaiduMapService.Instance.RoutePoints.Count - 1)
        {
            currentRoadIndex++;
            UpdateNavigationInfo(currentRoadIndex);
            Debug.Log($"切换到新路径点: {currentRoadIndex}");
        }

        // 检测是否偏离路径（基于距离）
        if (distanceToTarget > targetChangeRange * 2 && Time.time - lastReplanTime > replanCooldown)
        {
            onStatusUpdate?.Invoke("检测到路径偏离，正在重新规划...");
            Debug.Log($"路径偏离，距离: {distanceToTarget:F2} 米，重新规划...");
            StartNavigationInternal(currentPositionBD);
            lastReplanTime = Time.time;
        }
    }

    private void UpdateNavigationInfo(int index)
    {
        if (index >= BaiduMapService.Instance.RoutePoints.Count) return;
        string instruction = BaiduMapService.Instance.RoutePoints[index].instruction;

        // 提取转向信息
        naviMessage.TurnInstruction = ExtractTurnInstruction(instruction);
        naviMessage.IconID = GetIconID(naviMessage.TurnInstruction);

        // 提取距离信息
        naviMessage.DistanceText = ExtractDistance(instruction) ?? "未知距离";

        // 提取当前道路和下一道路
        naviMessage.CurrentRoad = ExtractRoadName(instruction, "沿<b>", "</b>") ?? naviMessage.CurrentRoad;
        naviMessage.NextRoad = ExtractRoadName(instruction, "进入<b>", "</b>") ?? naviMessage.NextRoad;

        onNavigationUpdate?.Invoke(naviMessage);
    }

    private string ExtractRoadName(string text, string startTag, string endTag)
    {
        if (!text.Contains(startTag) || !text.Contains(endTag)) return null;
        int start = text.IndexOf(startTag) + startTag.Length;
        int end = text.IndexOf(endTag, start);
        return text.Substring(start, end - start);
    }

    private string ExtractTurnInstruction(string instruction)
    {
        if (instruction.Contains("直行")) return "直行";
        if (instruction.Contains("左转")) return "左转";
        if (instruction.Contains("右转")) return "右转";
        if (instruction.Contains("靠左")) return "靠左";
        if (instruction.Contains("靠右")) return "靠右";
        if (instruction.Contains("掉头")) return "掉头";
        if (instruction.Contains("环岛")) return "进入环岛";
        if (instruction.Contains("斜左")) return "斜左";
        if (instruction.Contains("斜右")) return "斜右";
        if (instruction.Contains("主路")) return "进入主路";
        if (instruction.Contains("辅路")) return "进入辅路";
        return "未知";
    }

    private int GetIconID(string turnInstruction)
    {
        return turnInstruction switch
        {
            "直行" => 0,
            "左转" => 1,
            "右转" => 2,
            "靠左" => 3,
            "靠右" => 4,
            "掉头" => 5,
            "进入环岛" => 6,
            "斜左" => 7,
            "斜右" => 8,
            "进入主路" => 9,
            "进入辅路" => 10,
            _ => -1
        };
    }

    private string ExtractDistance(string text)
    {
        // 使用正则表达式匹配“行驶”后的数字和单位（米或公里）
        string pattern = @"行驶\s*(\d+\.?\d*)\s*(米|公里)";
        Match match = Regex.Match(text, pattern);
        if (match.Success)
        {
            double distance = double.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value;
            if (unit == "公里")
            {
                distance *= 1000; // 转换为米
            }
            return $"{distance:F0}米";
        }

        // 处理非标准格式，如“继续行驶”或“沿路行驶XX米”
        if (text.Contains("继续行驶") || text.Contains("沿路行驶"))
        {
            Match fallbackMatch = Regex.Match(text, @"(\d+\.?\d*)\s*(米|公里)");
            if (fallbackMatch.Success)
            {
                double distance = double.Parse(fallbackMatch.Groups[1].Value);
                string unit = fallbackMatch.Groups[2].Value;
                if (unit == "公里")
                {
                    distance *= 1000;
                }
                return $"{distance:F0}米";
            }
        }

        // 如果无法提取，返回基于路径点的估计距离
        if (currentRoadIndex < BaiduMapService.Instance.RoutePoints.Count)
        {
            Vector2d currentPositionBD = Utilities.WGS84ToBD09(currentPosition.x, currentPosition.y);
            double distance = Utilities.CalculateLongitudeLatitudeDistance(
                currentPositionBD,
                BaiduMapService.Instance.RoutePoints[currentRoadIndex].coordinate
            ) * 100000; // 转换为米
            return $"{distance:F0}米";
        }

        return null;
    }
}

