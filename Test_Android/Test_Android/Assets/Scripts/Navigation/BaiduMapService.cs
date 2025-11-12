using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using SimpleJSON;
using Utils;
using System.Collections.Generic;
using System.IO;
using System.Xml;

public class BaiduMapService : MonoBehaviour
{
    public static BaiduMapService Instance { get; private set; }
    private string baiduApiKey = "XXXXXXXXXXXXXXXXXXXXXXXXXX"; // 替换为您的 AK
    public string Region = "西安"; // 默认城市
    public bool IsMapReady { get; private set; } = false;
    public List<RoutePointInfo> RoutePoints { get; private set; } = new List<RoutePointInfo>();
    public bool UseLocalMap { get; private set; } = false;
    private string localRouteFile = "";

    void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ParseConfigData();
    }

    private void ParseConfigData()
    {
        string configPath = Path.Combine(Application.streamingAssetsPath, "ScenarioConfig.xml");
        if (!File.Exists(configPath))
        {
            Debug.LogWarning("配置文件未找到: " + configPath);
            return;
        }

        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configPath);
        XmlNode root = xmlDoc.SelectSingleNode("ScenarioConfig");
        XmlNode routeNode = root?.SelectSingleNode("PlanedRoute");
        if (routeNode != null && routeNode.Attributes["RouteData"] != null)
        {
            localRouteFile = routeNode.Attributes["RouteData"].Value;
            if (!string.IsNullOrEmpty(localRouteFile))
            {
                UseLocalMap = true;
                LoadRouteFromFile(Path.Combine(Application.streamingAssetsPath, localRouteFile));
            }
        }
        else
        {
            Debug.Log("配置文件中未找到 PlanedRoute");
        }
    }

    public IEnumerator GetCoordinatesFromAddress(string address, System.Action<string> onSuccess, System.Action<string> onError)
    {
        string encodedAddress = UnityWebRequest.EscapeURL(address);
        string url = $"https://api.map.baidu.com/geocoding/v3/?address={encodedAddress}&output=json&ak={baiduApiKey}";
        int retryCount = 3;

        while (retryCount > 0)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    JSONNode json = JSON.Parse(www.downloadHandler.text);
                    if (json["status"].AsInt == 0)
                    {
                        double lon = json["result"]["location"]["lng"].AsFloat;
                        double lat = json["result"]["location"]["lat"].AsFloat;
                        string destination = $"{lat},{lon}"; // 直接使用 BD-09LL 坐标
                        onSuccess?.Invoke(destination);
                        yield break;
                    }
                    else
                    {
                        onError?.Invoke($"地址编码失败: {json["message"]}");
                        retryCount--;
                        yield return new WaitForSeconds(1f);
                    }
                }
                else
                {
                    onError?.Invoke($"API 调用失败: {www.error}");
                    retryCount--;
                    yield return new WaitForSeconds(1f);
                }
            }
        }
        onError?.Invoke("地址编码失败：多次重试后仍无法连接");
    }

    public IEnumerator GetNavigationPath(Vector2d start, string destination, System.Action onSuccess, System.Action<string> onError)
    {
        if (UseLocalMap && RoutePoints.Count > 0)
        {
            IsMapReady = true;
            onSuccess?.Invoke();
            yield break;
        }

        Vector2d startBD = Utilities.WGS84ToBD09(start.x, start.y);
        string[] destCoords = destination.Split(',');
        Vector2d destBD = Utilities.WGS84ToBD09(double.Parse(destCoords[0]), double.Parse(destCoords[1]));
        string origin = $"{startBD.x},{startBD.y}";
        string dest = $"{destBD.x},{destBD.y}";
        string url = $"https://api.map.baidu.com/directionlite/v1/driving?origin={origin}&destination={dest}&output=json&ak={baiduApiKey}";
        int retryCount = 3;

        while (retryCount > 0)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    ParsePathData(www.downloadHandler.text);
                    if (RoutePoints.Count > 0)
                    {
                        IsMapReady = true;
                        onSuccess?.Invoke();
                        yield break;
                    }
                    else
                    {
                        onError?.Invoke("未解析到有效路径");
                        retryCount--;
                        yield return new WaitForSeconds(1f);
                    }
                }
                else
                {
                    onError?.Invoke($"API 调用失败: {www.error}");
                    retryCount--;
                    yield return new WaitForSeconds(1f);
                }
            }
        }
        onError?.Invoke("路径规划失败：多次重试后仍无法连接");
    }


    public IEnumerator GetCurrentRoadName(Vector2d currentPos, System.Action<string> onSuccess, System.Action<string> onError)
    {
        Vector2d currentCoordBD = Utilities.WGS84ToBD09(currentPos.x, currentPos.y);
        string sCurrentPos = $"{currentCoordBD.x},{currentCoordBD.y}";
        string roadURL = $"https://api.map.baidu.com/geocoder?location={sCurrentPos}&region={Region}&coord_type=bd09ll&output=json&extensions_road=true&ak={baiduApiKey}";

        using (UnityWebRequest www = UnityWebRequest.Get(roadURL))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                JSONNode json = JSON.Parse(www.downloadHandler.text);
                if (json["status"].AsInt == 0 && json["result"]["roads"].Count > 0)
                {
                    string roadName = json["result"]["roads"][0]["name"];
                    onSuccess?.Invoke(roadName);
                }
                else
                {
                    onError?.Invoke("无法获取道路名称");
                }
            }
            else
            {
                onError?.Invoke($"道路查询失败: {www.error}");
            }
        }
    }

    private void ParsePathData(string jsonData)
    {
        RoutePoints.Clear();
        JSONNode json = JSON.Parse(jsonData);
        if (json["status"].AsInt != 0)
        {
            Debug.LogError($"API 返回错误: {json["message"]}");
            return;
        }

        JSONArray steps = json["result"]["routes"][0]["steps"].AsArray;
        int stepIndex = 0;
        foreach (JSONNode step in steps)
        {
            string path = step["path"];
            string instruction = step["instruction"];
            string[] points = path.Split(';');
            foreach (string point in points)
            {
                string[] coords = point.Split(',');
                if (coords.Length < 2) continue;
                double lon = double.Parse(coords[0]);
                double lat = double.Parse(coords[1]);
                Vector2d bdCoord = new Vector2d(lat, lon); // 直接使用 BD-09LL 坐标
                RoutePoints.Add(new RoutePointInfo(bdCoord, instruction, stepIndex));
            }
            stepIndex++;
        }
    }

    private void LoadRouteFromFile(string filename)
    {
        if (!File.Exists(filename))
        {
            Debug.LogError("路径文件未找到: " + filename);
            return;
        }

        RoutePoints.Clear();
        string jsonData = File.ReadAllText(filename);
        JSONNode json = JSON.Parse(jsonData);
        if (json == null)
        {
            Debug.LogError("无法解析路径文件: " + filename);
            return;
        }

        JSONArray steps = json["result"]["routes"][0]["steps"].AsArray;
        int stepIndex = 0;
        foreach (JSONNode step in steps)
        {
            string path = step["path"];
            string instruction = step["instruction"];
            string[] points = path.Split(';');
            foreach (string point in points)
            {
                string[] coords = point.Split(',');
                if (coords.Length < 2) continue;
                double lon = double.Parse(coords[0]);
                double lat = double.Parse(coords[1]);
                Vector2d bdCoord = new Vector2d(lat, lon); // 直接使用 BD-09LL 坐标
                RoutePoints.Add(new RoutePointInfo(bdCoord, instruction, stepIndex));
            }
            stepIndex++;
        }
        IsMapReady = true;
    }

    public struct RoutePointInfo
    {
        public Vector2d coordinate;
        public string instruction;
        public int stepIndex;

        public RoutePointInfo(Vector2d coord, string instr, int step)
        {
            coordinate = coord;
            instruction = instr;
            stepIndex = step;
        }
    }
}


