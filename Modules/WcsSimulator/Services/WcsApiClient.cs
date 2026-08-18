using System.Net.Http.Json;
using System.Text.Json;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// GrcsBackend（8230）管理面 API 客户端（Skill E：前端只读 API + 展示）。
/// BaseAddress 取 localStorage grcs_wcs_url（保留的 UI 偏好），缺省 http://localhost:8230。
/// </summary>
public class WcsApiClient
{
    private readonly HttpClient _http;
    private readonly LocalStoreService _store;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public WcsApiClient(HttpClient http, LocalStoreService store)
    {
        _http = http;
        _store = store;
    }

    public string BaseUrl
    {
        get
        {
            var url = _store["grcs_wcs_url"];
            return string.IsNullOrEmpty(url) || url == "null" ? "http://localhost:8230" : url;
        }
    }

    private string U(string path) => BaseUrl.TrimEnd('/') + path;

    public async Task<T?> GetAsync<T>(string path)
    {
        try
        {
            var json = await _http.GetStringAsync(U(path));
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch { return default; }
    }

    public async Task<T?> PostAsync<TReq, T>(string path, TReq body)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(U(path), body);
            var json = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode ? JsonSerializer.Deserialize<T>(json, JsonOpts) : default;
        }
        catch { return default; }
    }

    public async Task<string> PostAsync<TReq>(string path, TReq body)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(U(path), body);
            var json = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode) return json;
            return JsonSerializer.Serialize(new { error = $"HTTP {(int)resp.StatusCode}" });
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { error = ex.Message }); }
    }

    public async Task<T?> PutAsync<TReq, T>(string path, TReq body)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync(U(path), body);
            var json = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode ? JsonSerializer.Deserialize<T>(json, JsonOpts) : default;
        }
        catch { return default; }
    }

    public async Task<bool> DeleteAsync(string path)
    {
        try { return (await _http.DeleteAsync(U(path))).IsSuccessStatusCode; }
        catch { return false; }
    }
}
