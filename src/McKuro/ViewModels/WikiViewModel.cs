using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Game;
using McKuro.Core.Models.Wiki;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>封面主图条目(启动器轮播优先,wiki banner 兜底),可带跳转链接。</summary>
public sealed class WikiBannerItem
{
    public required string Url { get; init; }
    public string Title { get; init; } = "";
    public string JumpUrl { get; init; } = "";
    public bool HasJump => !string.IsNullOrWhiteSpace(JumpUrl);
}

/// <summary>启动器公告组件(活动/公告/新闻)的文本条目,点击跳转网页。</summary>
public sealed class LauncherNoticeItem
{
    public required string Title { get; init; }
    public required string TimeText { get; init; }
    public required string Url { get; init; }
}

/// <summary>库街区官方资讯卡片(封面 + 标题 + 日期),点击跳转帖子详情页。</summary>
public sealed class OfficialEventCard
{
    public required string Title { get; init; }
    public required string CoverUrl { get; init; }
    public required string DateText { get; init; }
    public required string Url { get; init; }
}

/// <summary>图鉴网页快捷入口。</summary>
public sealed record WikiLinkItem(string Name, string Url, string Description);

/// <summary>
/// 图鉴页(重新设计):
/// ① 封面主图轮播(官方启动器 slideshow 优先,wiki banner 兜底),点击跳转;
/// ② 库街区官方资讯卡片(资讯/公告/活动三页签,/forum/companyEvent/findEventList,免登录),
///    点击打开 https://www.kurobbs.com/mc/post/{postId} 详情页;
/// ③ 启动器公告组件(活动/公告/新闻三页签,gamestarter information 接口,Haiyu 同源),点击跳转 jumpUrl。
/// </summary>
public sealed partial class WikiViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _selectedKurobbsTab;

    [ObservableProperty]
    private int _selectedLauncherTab;

    /// <summary>封面主图轮播。</summary>
    public ObservableCollection<WikiBannerItem> Banners { get; } = [];

    /// <summary>库街区·资讯(eventType=2)。</summary>
    public ObservableCollection<OfficialEventCard> KurobbsNews { get; } = [];

    /// <summary>库街区·公告(eventType=3)。</summary>
    public ObservableCollection<OfficialEventCard> KurobbsAnnouncements { get; } = [];

    /// <summary>库街区·活动(eventType=1)。</summary>
    public ObservableCollection<OfficialEventCard> KurobbsActivities { get; } = [];

    /// <summary>启动器公告·活动。</summary>
    public ObservableCollection<LauncherNoticeItem> LauncherActivities { get; } = [];

    /// <summary>启动器公告·公告。</summary>
    public ObservableCollection<LauncherNoticeItem> LauncherNotices { get; } = [];

    /// <summary>启动器公告·新闻。</summary>
    public ObservableCollection<LauncherNoticeItem> LauncherNews { get; } = [];

    /// <summary>网页快捷入口。</summary>
    public IReadOnlyList<WikiLinkItem> WebLinks { get; } =
    [
        new("库街区官方页", "https://www.kurobbs.com/mc/official", "公告 / 资讯 / 活动官方发布"),
        new("库街区 Wiki", "https://wiki.kurobbs.com/mc/home", "官方角色/武器/声骸图鉴"),
        new("库街区地图", "https://www.kurobbs.com/mc/map/", "官方大地图 / 资源分布"),
        new("Gamekee Wiki", "https://www.gamekee.com/mc/", "第三方图鉴与攻略"),
    ];

    public WikiViewModel()
    {
        // 进入页面自动加载(对齐其他页面的自动刷新)
        _ = LoadAsync();
    }

    /// <summary>在默认浏览器中打开网页。</summary>
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

    [RelayCommand]
    private Task LoadAsync() => LoadInternalAsync();

    /// <summary>服务器渠道:与启动器页一致(设置优先,自动检测兜底)。</summary>
    private static GameServerType ServerType
    {
        get
        {
            var configured = AppServices.Settings.Current.ServerType;
            return configured == GameServerType.Unknown
                ? AppServices.Paths.DetectServerType()
                : configured;
        }
    }

    private async Task LoadInternalAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在加载图鉴与官方资讯…";
        try
        {
            Banners.Clear();
            KurobbsNews.Clear();
            KurobbsAnnouncements.Clear();
            KurobbsActivities.Clear();
            LauncherActivities.Clear();
            LauncherNotices.Clear();
            LauncherNews.Clear();

            int banners = await LoadBannersAsync();
            var (news, anns, acts) = await LoadKurobbsEventsAsync();
            int notices = await LoadLauncherGuidanceAsync();

            StatusText = $"已加载:封面 {banners} · 库街区 资讯{news}/公告{anns}/活动{acts} · 启动器公告 {notices} 条";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>封面主图:官方启动器轮播图优先(带跳转),失败回退 wiki 首页 banner。返回数量。</summary>
    private async Task<int> LoadBannersAsync()
    {
        var info = await AppServices.LauncherInfo.GetLauncherInfoAsync(ServerType);
        if (info?.Slideshow is { Count: > 0 })
        {
            foreach (var slide in info.Slideshow.Where(s => !string.IsNullOrWhiteSpace(s.Url)))
            {
                Banners.Add(new WikiBannerItem
                {
                    Url = slide.Url,
                    Title = slide.CarouselNotes ?? "",
                    JumpUrl = slide.JumpUrl ?? "",
                });
            }
            return Banners.Count;
        }

        // 兜底:wiki 首页 banner
        var home = await AppServices.Wiki.GetHomePageAsync(WikiType.Waves);
        foreach (var banner in home?.Data?.ContentJson?.Banner ?? [])
        {
            if (!string.IsNullOrWhiteSpace(banner.Url))
            {
                Banners.Add(new WikiBannerItem { Url = banner.Url!, Title = banner.Title ?? "" });
            }
        }
        return Banners.Count;
    }

    /// <summary>拉取库街区官方资讯三个分类。返回 (资讯数, 公告数, 活动数)。</summary>
    private async Task<(int News, int Anns, int Acts)> LoadKurobbsEventsAsync()
    {
        var newsTask = AppServices.Wiki.GetOfficialEventsAsync(eventType: 2);
        var annTask = AppServices.Wiki.GetOfficialEventsAsync(eventType: 3);
        var actTask = AppServices.Wiki.GetOfficialEventsAsync(eventType: 1);
        await Task.WhenAll(newsTask, annTask, actTask).ConfigureAwait(false);

        int Fill(ObservableCollection<OfficialEventCard> target, List<OfficialEventItem>? items)
        {
            foreach (var item in items ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.PostTitle))
                {
                    continue;
                }
                target.Add(new OfficialEventCard
                {
                    Title = item.PostTitle!.Trim(),
                    CoverUrl = item.CoverUrl ?? "",
                    DateText = FormatDate(item.ShelveTime),
                    Url = $"https://www.kurobbs.com/mc/post/{item.PostId}",
                });
            }
            return target.Count;
        }

        // 回到 UI 线程再填充 ObservableCollection
        var news = await newsTask.ConfigureAwait(true);
        var anns = await annTask.ConfigureAwait(true);
        var acts = await actTask.ConfigureAwait(true);
        return (Fill(KurobbsNews, news), Fill(KurobbsAnnouncements, anns), Fill(KurobbsActivities, acts));
    }

    /// <summary>启动器公告组件(Haiyu 左下角同源数据):活动/公告/新闻三组。返回总条数。</summary>
    private async Task<int> LoadLauncherGuidanceAsync()
    {
        var info = await AppServices.LauncherInfo.GetLauncherInfoAsync(ServerType);
        var guidance = info?.Guidance;
        if (guidance is null)
        {
            return 0;
        }

        void Fill(ObservableCollection<LauncherNoticeItem> target, AnnouncementGroup? group)
        {
            foreach (var item in group?.Contents ?? [])
            {
                var title = StripHtml(item.Content);
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }
                target.Add(new LauncherNoticeItem
                {
                    Title = title,
                    TimeText = item.Time ?? "",
                    Url = item.JumpUrl ?? "",
                });
            }
        }

        Fill(LauncherActivities, guidance.Activity);
        Fill(LauncherNotices, guidance.Notice);
        Fill(LauncherNews, guidance.News);
        return LauncherActivities.Count + LauncherNotices.Count + LauncherNews.Count;
    }

    /// <summary>Unix 毫秒 → "yyyy-MM-dd"。</summary>
    private static string FormatDate(long shelveTimeMs)
    {
        if (shelveTimeMs <= 0)
        {
            return "";
        }
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(shelveTimeMs).LocalDateTime.ToString("yyyy-MM-dd");
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>剥离 HTML 标签、解码实体并压缩空白,超长截断(公告 content 可能是富文本)。</summary>
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        if (html.Contains('<'))
        {
            html = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        }
        var text = WebUtility.HtmlDecode(html);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 60 ? text[..60] + "…" : text;
    }
}
