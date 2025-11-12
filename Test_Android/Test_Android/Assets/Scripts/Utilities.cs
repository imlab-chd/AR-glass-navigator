
using UnityEngine;
using UnityEngine.Networking;
using System;
using Utils;

public class Utilities : MonoBehaviour
{
    private const string BAIDU_API_KEY = "9dT81x9S4RQ7oq6tvZDaXFfUb2LZRTLc"; // 百度地图API密钥
    private const string BAIDU_GEOCONV_URL = "https://api.map.baidu.com/geoconv/v2/";
    private const string BAIDU_DISTANCE_URL = "http://api.map.baidu.com/directionlite/v1/driving?";


    // 验证输入的经纬度格式是否有效
    static public bool ValidateDestinationInput(string input)
    {
        string[] parts = input.Split(',');
        if (parts.Length != 2) return false;
        if (double.TryParse(parts[0], out double lat) && double.TryParse(parts[1], out double lon))
        {
            return lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
        }
        return false;
    }

    public static double CalculateLongitudeLatitudeDistance(Vector2d start, Vector2d end)
    {

        // 构造百度地图 directionlite/v1/driving API 请求
        string url = $"{BAIDU_DISTANCE_URL}?" +
                     $"origin={start.x},{start.y}&destination={end.x},{end.y}" +
                     $"&coord_type=bd09ll&output=json&ak={BAIDU_API_KEY}";

        int retryCount = 3;
        while (retryCount > 0)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SendWebRequest();
                while (!request.isDone) { } // 阻塞等待请求完成

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string json = request.downloadHandler.text;
                        var response = JsonUtility.FromJson<BaiduDistanceResponse>(json);

                        if (response.status == 0 && response.result?.routes?.Length > 0)
                        {
                            return response.result.routes[0].distance; // 返回距离（米）
                        }
                        else
                        {
                            Debug.LogError($"百度API返回错误: 状态码 {response.status}, 消息: {response.message}");
                            retryCount--;
                            System.Threading.Thread.Sleep(1000); // 等待 1 秒后重试
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"解析响应失败: {e.Message}");
                        retryCount--;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                else
                {
                    Debug.LogError($"API 请求失败: {request.error}");
                    retryCount--;
                    System.Threading.Thread.Sleep(1000);
                }
            }
        }

        Debug.LogError("距离计算失败：多次重试后仍无法连接");
        return 0;
    }

    // WGS-84 转 BD-09LL（同步，使用百度地图API，model=2）
    public static Vector2d WGS84ToBD09(double lat, double lon)
    {
        string url = $"{BAIDU_GEOCONV_URL}?coords={lon},{lat}&from=1&to=5&model=2&ak={BAIDU_API_KEY}";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SendWebRequest();
            while (!request.isDone) { } // 阻塞等待请求完成

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"WGS84ToBD09 失败: {request.error}");
                return new Vector2d(lat, lon); // 返回原始坐标作为备用
            }

            try
            {
                string json = request.downloadHandler.text;
                BaiduGeoconvResponse response = JsonUtility.FromJson<BaiduGeoconvResponse>(json);
                if (response.status == 0 && response.result.Length > 0)
                {
                    double bdLon = response.result[0].x;
                    double bdLat = response.result[0].y;
                    return new Vector2d(bdLat, bdLon);
                }
                else
                {
                    Debug.LogError($"WGS84ToBD09 API 错误: 状态码 {response.status}");
                    return new Vector2d(lat, lon);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"WGS84ToBD09 JSON 解析错误: {e.Message}");
                return new Vector2d(lat, lon);
            }
        }
    }

    // HTML 转纯文本
    public static string HTML2Text(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", "");
    }

    // 用于解析百度地图API的响应（坐标转换）
    [Serializable]
    private class BaiduGeoconvResponse
    {
        public int status;
        public BaiduCoord[] result;
    }

    [Serializable]
    private class BaiduCoord
    {
        public double x; // 经度
        public double y; // 纬度
    }

    // 用于解析百度地图测距API的响应
    [Serializable]
    private class BaiduDistanceResponse
    {
        public int status;
        public string message;
        public Result result;

        [Serializable]
        public class Result
        {
            public Route[] routes;

            [Serializable]
            public class Route
            {
                public int distance; // 距离（米）
                public int duration; // 时间（秒）
            }
        }
    }
}