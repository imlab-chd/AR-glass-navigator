using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Utils;

public class Test : MonoBehaviour
{
    public enum Mode { Server, Client }
    public Mode mode = Mode.Server; // 在 Inspector 中选择运行模式
    public UDPServer udpServer; // 服务端使用的 UDPServer
    public string startAddress = "西安钟楼"; // 在编辑器中输入起始地址
    public string destinationAddress = "西安大雁塔"; // 在编辑器中输入目的地址
    public float switchInterval = 1f; // 路径点切换间隔（秒）
    private float lastSwitchTime;
    private int currentPointIndex = 0; // 当前路径点索引
    private List<BaiduMapService.RoutePointInfo> routePoints; // 保存路径点

    void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        StartCoroutine(FetchRoute());
    }

    private IEnumerator FetchRoute()
    {
        // 获取起始地址的经纬度（BD-09LL）
        string startCoords = null;
        yield return BaiduMapService.Instance.GetCoordinatesFromAddress(startAddress,
            (result) => startCoords = result,
            (error) => Debug.LogError($"获取起始地址坐标失败: {error}"));

        if (string.IsNullOrEmpty(startCoords))
        {
            Debug.LogError("未能获取起始地址坐标，路径规划终止");
            yield break;
        }

        // 获取目的地址的经纬度（BD-09LL）
        string destinationCoords = null;
        yield return BaiduMapService.Instance.GetCoordinatesFromAddress(destinationAddress,
            (result) => destinationCoords = result,
            (error) => Debug.LogError($"获取目的地址坐标失败: {error}"));

        if (string.IsNullOrEmpty(destinationCoords))
        {
            Debug.LogError("未能获取目的地址坐标，路径规划终止");
            yield break;
        }

        // 解析起始坐标为 Vector2d
        string[] startParts = startCoords.Split(',');
        if (startParts.Length != 2 || !double.TryParse(startParts[0], out double startLat) || !double.TryParse(startParts[1], out double startLon))
        {
            Debug.LogError("起始地址坐标格式无效，路径规划终止");
            yield break;
        }
        Vector2d start = new Vector2d(startLat, startLon);

        // 验证目的地坐标格式
        if (!Utilities.ValidateDestinationInput(destinationCoords))
        {
            Debug.LogError("目的地址坐标格式无效，路径规划终止");
            yield break;
        }

        // 获取导航路径（输入 BD-09LL 坐标）
        yield return BaiduMapService.Instance.GetNavigationPath(start, destinationCoords,
            () =>
            {
                routePoints = BaiduMapService.Instance.RoutePoints;
                if (routePoints != null && routePoints.Count > 0)
                {
                    Debug.Log($"路径规划成功，路径点数: {routePoints.Count}");
                    lastSwitchTime = Time.time;
                }
                else
                {
                    Debug.LogError("路径点为空，导航失败");
                }
            },
            (error) =>
            {
                Debug.LogError($"路径规划失败: {error}");
            });

        // 等待导航路径加载完成
        yield return new WaitUntil(() => BaiduMapService.Instance.IsMapReady);
    }

    void Update()
    {
        if (mode == Mode.Server && routePoints != null && routePoints.Count > 0)
        {
            if (Time.time - lastSwitchTime >= switchInterval)
            {
                SendNavigationMessage();
                lastSwitchTime = Time.time;
                currentPointIndex = (currentPointIndex + 1) % routePoints.Count; // 循环路径点
            }
        }
    }

    private void SendNavigationMessage()
    {
        if (currentPointIndex >= routePoints.Count)
        {
            Debug.LogWarning("路径点索引超出范围");
            return;
        }

        var point = routePoints[currentPointIndex];
        // 构造 NavigationMessage
        NavigationMessage msg = new NavigationMessage
        {
            TurnInstruction = ExtractTurnInstruction(point.instruction),
            IconID = GetIconID(point.instruction),
            DistanceText = ExtractDistance(point.instruction) ?? EstimateDistance(currentPointIndex),
            CurrentRoad = ExtractRoadName(point.instruction, "沿<b>", "</b>") ?? "未知道路",
            NextRoad = (currentPointIndex + 1 < routePoints.Count)
                ? ExtractRoadName(routePoints[currentPointIndex + 1].instruction, "进入<b>", "</b>") ?? ""
                : ""
        };

        try
        {
            string json = JsonUtility.ToJson(msg);
            udpServer.SendNavigationMessage(msg);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"服务端发送失败: {e.Message}");
        }
    }

    private string ExtractTurnInstruction(string instruction)
    {
        if (string.IsNullOrEmpty(instruction)) return "未知";
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

    private int GetIconID(string instruction)
    {
        return ExtractTurnInstruction(instruction) switch
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

    private string ExtractRoadName(string instruction, string startTag, string endTag)
    {
        if (string.IsNullOrEmpty(instruction) || !instruction.Contains(startTag) || !instruction.Contains(endTag))
            return null;
        int start = instruction.IndexOf(startTag) + startTag.Length;
        int end = instruction.IndexOf(endTag, start);
        return instruction.Substring(start, end - start);
    }

    private string ExtractDistance(string instruction)
    {
        if (string.IsNullOrEmpty(instruction)) return null;

        // 使用正则表达式匹配“行驶”后的数字和单位（米或公里）
        string pattern = @"行驶\s*(\d+\.?\d*)\s*(米|公里)";
        Match match = Regex.Match(instruction, pattern);
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
        if (instruction.Contains("继续行驶") || instruction.Contains("沿路行驶"))
        {
            Match fallbackMatch = Regex.Match(instruction, @"(\d+\.?\d*)\s*(米|公里)");
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

        return null;
    }

    private string EstimateDistance(int index)
    {
        if (index >= routePoints.Count - 1) return "0米";
        double distance = Utilities.CalculateLongitudeLatitudeDistance(
            routePoints[index].coordinate,
            routePoints[index + 1].coordinate
        ) * 100000; // 转换为米
        return $"{distance:F0}米";
    }
}