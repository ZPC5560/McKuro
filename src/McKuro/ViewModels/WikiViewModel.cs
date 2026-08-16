using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Wiki;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>图鉴 banner 条目。</summary>
public sealed class WikiBannerItem
{
    public required string Url { get; init; }
    public required string Title { get; init; }
}

/// <summary>图鉴热点/公告条目。</summary>
public sealed class WikiTextItem
{
    public required string Title { get; init; }
    public string? Sub { get; init; }
    public string? ImageUrl { get; init; }
}

/// <summary>图鉴网页快捷入口。</summary>
public sealed record WikiLinkItem(string Name, string Url, string Description);

/// <summary>
/// 图鉴页:库街区图鉴首页数据(鸣潮),展示 Banner 轮播、公告、热点内容与活动。
/// 参考 Haiyu 的 WavesWikiPage。
/// </summary>
public sealed partial class WikiViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _selectedGameIndex;

    public ObservableCollection<WikiBannerItem> Banners { get; } = [];

    public ObservableCollection<WikiTextItem> Announcements { get; } = [];

    public ObservableCollection<WikiTextItem> HotContents { get; } = [];

    public ObservableCollection<WikiTextItem> Events { get; } = [];

    public IReadOnlyList<string> Games { get; } = ["鸣潮"];

    /// <summary>网页快捷入口(参考 WutheringWavesTool HomeView 的图鉴菜单)。</summary>
    public IReadOnlyList<WikiLinkItem> WebLinks { get; } =
    [
        new("库街区 Wiki", "https://wiki.kurobbs.com/mc/home", "官方角色/武器/声骸图鉴"),
        new("库街区地图", "https://www.kurobbs.com/mc/map/", "官方大地图 / 资源分布"),
        new("Gamekee Wiki", "https://www.gamekee.com/mc/", "第三方图鉴与攻略"),
        new("彩墨地图", "https://map.caimogu.cc/ww/main.html", "第三方大地图"),
    ];

    public WikiViewModel()
    {
        // 进入页面自动加载(对齐其他页面的自动刷新)
        _ = LoadAsync();
    }

    private WikiType CurrentType => WikiType.Waves;

    partial void OnSelectedGameIndexChanged(int value) => _ = LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => LoadInternalAsync();

    /// <summary>在默认浏览器中打开图鉴网页(参考 WutheringWavesTool 的 toWiki 系列)。</summary>
    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            StatusText = "打开网页失败";
        }
    }

    private async Task LoadInternalAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在加载图鉴…";
        try
        {
            var type = CurrentType;
            var home = await AppServices.Wiki.GetHomePageAsync(type);

            Banners.Clear();
            Announcements.Clear();
            HotContents.Clear();
            Events.Clear();

            if (home is not { Data.ContentJson: { } json })
            {
                StatusText = "图鉴数据加载失败";
                return;
            }

            if (json.Banner is not null)
            {
                foreach (var banner in json.Banner.Where(b => !string.IsNullOrWhiteSpace(b.Url)))
                {
                    Banners.Add(new WikiBannerItem { Url = banner.Url!, Title = banner.Title ?? "" });
                }
            }

            if (json.Announcement is not null)
            {
                foreach (var ann in json.Announcement)
                {
                    if (!string.IsNullOrWhiteSpace(ann.Content))
                    {
                        // 公告 content 是 HTML 富文本,剥离标签取纯文本作为说明
                        Announcements.Add(new WikiTextItem
                        {
                            Title = string.IsNullOrWhiteSpace(ann.Name) ? "公告" : ann.Name!,
                            Sub = StripHtml(ann.Content),
                        });
                    }
                }
            }

            var hots = await AppServices.Wiki.GetEventDataAsync(type);
            if (hots is not null)
            {
                foreach (var hot in hots.Where(h => !string.IsNullOrWhiteSpace(h.Title)))
                {
                    HotContents.Add(new WikiTextItem
                    {
                        Title = hot.Title!,
                        // 热点内容:用活动时间区间作为说明(而非文件名)
                        Sub = FormatDateRange(hot.CountDown?.DateRange),
                    });
                }
            }

            var events = await AppServices.Wiki.GetEventTabDataAsync(type);
            if (events?.Tabs is not null)
            {
                foreach (var tab in events.Tabs.Where(t => !string.IsNullOrWhiteSpace(t.Name)))
                {
                    Events.Add(new WikiTextItem
                    {
                        Title = tab.Name!,
                        Sub = tab.Description,
                        ImageUrl = tab.Images?.FirstOrDefault()?.Image,
                    });
                }
            }

            StatusText = $"图鉴已加载 (Banner {Banners.Count} · 公告 {Announcements.Count} · 热点 {HotContents.Count} · 活动 {Events.Count})";
        }
        catch (Exception ex)
        {
            StatusText = $"图鉴加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>剥离 HTML 标签与多余空白,取纯文本(公告 content 是富文本)。</summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 120 ? text[..120] + "…" : text;
    }

    /// <summary>格式化活动时间区间(如 ["2026-07-10 11:00","2026-08-19 03:59"] → "07-10 ~ 08-19")。</summary>
    private static string FormatDateRange(IReadOnlyList<string>? range)
    {
        if (range is not { Count: 2 } || string.IsNullOrWhiteSpace(range[0]) || string.IsNullOrWhiteSpace(range[1]))
        {
            return "";
        }
        static string Short(string s)
        {
            // "2026-07-10 11:00" → "07-10"
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && parts[0].Length >= 10 ? parts[0][5..10] : s;
        }
        return $"{Short(range[0])} ~ {Short(range[1])}";
    }
}
