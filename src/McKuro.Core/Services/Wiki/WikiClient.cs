using System.Text.Json;
using McKuro.Core.Models.Wiki;

namespace McKuro.Core.Services.Wiki;

/// <summary>图鉴 (wiki) 客户端:拉取库街区图鉴首页数据。</summary>
public sealed class WikiClient
{
    private const string HomePageUrl = "https://api.kurobbs.com/wiki/core/homepage/getPage";

    private readonly HttpClient _http;

    public WikiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>获取图鉴首页。</summary>
    public async Task<WikiHomeModel?> GetHomePageAsync(WikiType type, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, HomePageUrl);
            request.Headers.TryAddWithoutValidation("wiki_type", ((int)type).ToString());
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            request.Content = new StringContent("", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, WikiJsonContext.Default.WikiHomeModel);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>获取热点内容列表。</summary>
    public async Task<List<HotContentSide>?> GetEventDataAsync(WikiType type, CancellationToken ct = default)
    {
        var model = await GetHomePageAsync(type, ct).ConfigureAwait(false);
        var side = model?.Data?.ContentJson?.SideModules?
            .FirstOrDefault(x => x.Type == "hot-content-side");
        if (side?.Content is not { } element || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        return element.Deserialize(WikiJsonContext.Default.ListHotContentSide);
    }

    /// <summary>获取活动内容(单个 events-side,兼容旧调用)。</summary>
    public async Task<EventContentSide?> GetEventTabDataAsync(WikiType type, CancellationToken ct = default)
    {
        var list = await GetEventTabDataListAsync(type, ct).ConfigureAwait(false);
        return list?.FirstOrDefault();
    }

    /// <summary>获取全部活动内容(所有 events-side:角色/武器卡池等)。</summary>
    public async Task<List<EventContentSide>?> GetEventTabDataListAsync(WikiType type, CancellationToken ct = default)
    {
        var model = await GetHomePageAsync(type, ct).ConfigureAwait(false);
        var sides = model?.Data?.ContentJson?.SideModules?
            .Where(x => x.Type == "events-side")
            .ToList();
        if (sides is null || sides.Count == 0)
        {
            return null;
        }
        var result = new List<EventContentSide>(sides.Count);
        foreach (var side in sides)
        {
            if (side.Content is { } element && element.ValueKind == JsonValueKind.Object)
            {
                var parsed = element.Deserialize(WikiJsonContext.Default.EventContentSide);
                if (parsed is not null)
                {
                    result.Add(parsed);
                }
            }
        }
        return result.Count > 0 ? result : null;
    }
}
