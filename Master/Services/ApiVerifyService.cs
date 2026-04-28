using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SimulatorApp.Master.Services;

/// <summary>
/// 主站 API 比对服务。
/// 从指定 HTTP 接口拉取 JSON 数据，将所有数值字段展开为扁平字典，
/// 按变量名匹配并与 Modbus 轮询物理值进行数值比较。
/// </summary>
public static class ApiVerifyService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// 向 url 发送 GET 请求（携带 Authorization 头），
    /// 将响应 JSON 所有数值叶节点展开为 {路径 → double} 字典。
    /// </summary>
    public static async Task<Dictionary<string, double>> FetchNumericFieldsAsync(
        string url, string authorization, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(authorization))
            req.Headers.TryAddWithoutValidation("Authorization", authorization);

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);

        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        FlattenElement(doc.RootElement, string.Empty, result);
        return result;
    }

    /// <summary>
    /// 在 apiData 中查找与 variableName 匹配的值。
    /// 匹配规则：将路径按 '.' '[' ']' 分割后，任意一段与 variableName 大小写不敏感相等即命中。
    /// </summary>
    public static bool TryMatch(
        Dictionary<string, double> apiData, string variableName, out double apiValue)
    {
        apiValue = 0;
        if (string.IsNullOrWhiteSpace(variableName)) return false;

        foreach (var kv in apiData)
        {
            var segments = kv.Key.Split('.', '[', ']')
                             .Where(s => !string.IsNullOrEmpty(s));
            if (segments.Any(s => string.Equals(s, variableName, StringComparison.OrdinalIgnoreCase)))
            {
                apiValue = kv.Value;
                return true;
            }
        }
        return false;
    }

    // ── 私有 ────────────────────────────────────────────────────────────

    private static void FlattenElement(JsonElement el, string path, Dictionary<string, double> out_)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    string child = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                    FlattenElement(prop.Value, child, out_);
                }
                break;

            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in el.EnumerateArray())
                    FlattenElement(item, $"{path}[{i++}]", out_);
                break;

            case JsonValueKind.Number when el.TryGetDouble(out double d):
                if (!string.IsNullOrEmpty(path)) out_[path] = d;
                break;

            case JsonValueKind.String:
                if (!string.IsNullOrEmpty(path)
                    && double.TryParse(el.GetString(), NumberStyles.Any,
                                       CultureInfo.InvariantCulture, out double sd))
                    out_[path] = sd;
                break;
        }
    }
}
