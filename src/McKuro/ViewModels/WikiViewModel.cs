using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Game;
using McKuro.Core.Models.Wiki;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Launcher;
using McKuro.Services;

namespace McKuro.ViewModels;

    /// <summary>封面主图条目(启动器轮播优先,wiki banner 兜底);视频在原轮播控件内播放(直接视频=mpv,B站视频=内嵌播放器)。</summary>
public sealed class WikiBannerItem
{
    public required string Url { get; init; }
    public string Title { get; init; } = "";
    public string JumpUrl { get; init; } = "";
    public bool HasJump => !string.IsNullOrWhiteSpace(JumpUrl);

    /// <summary>跳转链接为B站视频时解析出的 BV 号(非空 = 在原控件内嵌B站播放器自动播放)。</summary>
    public string? Bvid { get; init; }
    public bool IsBiliVideo => !string.IsNullOrEmpty(Bvid);

    /// <summary>链接本身是直接视频文件(按扩展名识别,mp4/webm/mov/m3u8 等)→ mpv 原地播放。</summary>
    public bool IsVideo => WikiBannerItem.IsVideoUrl(Url);

    /// <summary>按扩展名判断是否视频文件链接。</summary>
    public static bool IsVideoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }
        var path = url.Contains('?') ? url[..url.IndexOf('?')] : url;
        return path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>启动器公告组件(活动/公告/新闻)的条目(封面 + 标题 + 日期),点击跳转网页。</summary>
public sealed class LauncherNoticeItem
{
    public required string Title { get; init; }
    public required string TimeText { get; init; }
    public required string Url { get; init; }

    /// <summary>封面主图(从公告富文本 content 中提取的首张图片;无图时为空,显示占位)。</summary>
    public string CoverUrl { get; init; } = "";
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

    /// <summary>当前轮播索引(视频覆盖层跟随当前项切换)。</summary>
    [ObservableProperty]
    private int _selectedBannerIndex;

    /// <summary>当前轮播项(无数据为 null)。</summary>
    public WikiBannerItem? CurrentBanner =>
        SelectedBannerIndex >= 0 && SelectedBannerIndex < Banners.Count ? Banners[SelectedBannerIndex] : null;

    /// <summary>当前项是直接视频文件(mp4 等)→ mpv 原地自动播放。</summary>
    public bool CurrentBannerIsDirectVideo => CurrentBanner?.IsVideo == true;

    /// <summary>当前项是B站视频 → 原控件内嵌B站播放器自动播放。</summary>
    public bool CurrentBannerIsBili => CurrentBanner?.IsBiliVideo == true;

    public string CurrentBannerTitle => CurrentBanner?.Title ?? "";
    public bool CurrentBannerHasTitle => CurrentBannerTitle.Length > 0;
    public string CurrentBannerJumpUrl => CurrentBanner?.JumpUrl ?? "";
    public bool CurrentBannerHasJump => CurrentBannerJumpUrl.Length > 0;

    private void RaiseCurrentBannerChanged()
    {
        OnPropertyChanged(nameof(CurrentBanner));
        OnPropertyChanged(nameof(CurrentBannerIsDirectVideo));
        OnPropertyChanged(nameof(CurrentBannerIsBili));
        OnPropertyChanged(nameof(CurrentBannerTitle));
        OnPropertyChanged(nameof(CurrentBannerHasTitle));
        OnPropertyChanged(nameof(CurrentBannerJumpUrl));
        OnPropertyChanged(nameof(CurrentBannerHasJump));
    }

    partial void OnSelectedBannerIndexChanged(int value) => RaiseCurrentBannerChanged();

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

    /// <summary>封面反查表(postId → 封面):来自 findEventList 全量,供启动器公告组件匹配封面。</summary>
    private readonly Dictionary<string, string> _coverByPostId = new();

    /// <summary>封面反查表(标题 → 封面,精确匹配兜底)。</summary>
    private readonly Dictionary<string, string> _coverByTitle = new();

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
            _coverByPostId.Clear();
            _coverByTitle.Clear();

            int banners = await LoadBannersAsync();
            // 重置轮播到首项并刷新视频覆盖层状态(Banners 重建后索引不变也要通知)
            SelectedBannerIndex = 0;
            RaiseCurrentBannerChanged();
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

    /// <summary>封面主图:官方启动器轮播图优先(带跳转),失败回退 wiki 首页 banner。返回数量。
    /// 跳转链接为B站视频(页面/短链)时并行解析出 BV 号,供原控件内嵌播放器播放。</summary>
    private async Task<int> LoadBannersAsync()
    {
        var info = await AppServices.LauncherInfo.GetLauncherInfoAsync(ServerType);
        if (info?.Slideshow is { Count: > 0 })
        {
            var slides = info.Slideshow.Where(s => !string.IsNullOrWhiteSpace(s.Url)).ToList();
            // 并行解析B站视频链接(含 b23.tv 短链重定向)→ BV 号
            var bvids = new string?[slides.Count];
            await Task.WhenAll(slides.Select(async (slide, i) =>
            {
                if (!BiliVideoHelper.IsBiliVideoUrl(slide.JumpUrl))
                {
                    return;
                }
                try
                {
                    bvids[i] = await BiliVideoHelper.ResolveBvIdAsync(slide.JumpUrl, AppServices.Http);
                }
                catch (Exception)
                {
                    // 解析失败:该轮播项按普通图片展示
                }
            }));
            for (var i = 0; i < slides.Count; i++)
            {
                Banners.Add(new WikiBannerItem
                {
                    Url = slides[i].Url,
                    Title = slides[i].CarouselNotes ?? "",
                    JumpUrl = slides[i].JumpUrl ?? "",
                    Bvid = bvids[i],
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
        // 拉全量(50)用于启动器公告封面反查,卡片展示取前 12
        var newsTask = AppServices.Wiki.GetOfficialEventsAsync(eventType: 2, pageSize: 50);
        var annTask = AppServices.Wiki.GetOfficialEventsAsync(eventType: 3, pageSize: 50);
        var actTask = AppServices.Wiki.GetOfficialEventsAsync(eventType: 1, pageSize: 50);
        await Task.WhenAll(newsTask, annTask, actTask).ConfigureAwait(false);

        int Fill(ObservableCollection<OfficialEventCard> target, List<OfficialEventItem>? items)
        {
            foreach (var item in (items ?? []).Take(12))
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

        void IndexCover(List<OfficialEventItem>? items)
        {
            foreach (var item in items ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.CoverUrl))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(item.PostTitle))
                {
                    _coverByTitle[item.PostTitle.Trim()] = item.CoverUrl!;
                }
                if (!string.IsNullOrWhiteSpace(item.PostId))
                {
                    _coverByPostId[item.PostId] = item.CoverUrl!;
                }
            }
        }

        // 回到 UI 线程再填充 ObservableCollection
        var news = await newsTask.ConfigureAwait(true);
        var anns = await annTask.ConfigureAwait(true);
        var acts = await actTask.ConfigureAwait(true);
        IndexCover(news);
        IndexCover(anns);
        IndexCover(acts);
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
                // 封面:优先按 jumpUrl 的 postId 反查库街区官方资讯,其次按标题精确匹配,最后回退 content 首图
                var postId = ExtractPostId(item.JumpUrl);
                var cover = postId.Length > 0 && _coverByPostId.TryGetValue(postId, out var c1)
                    ? c1
                    : _coverByTitle.TryGetValue(title, out var c2)
                        ? c2
                        : ExtractFirstImage(item.Content);
                target.Add(new LauncherNoticeItem
                {
                    Title = title,
                    TimeText = item.Time ?? "",
                    Url = item.JumpUrl ?? "",
                    CoverUrl = cover,
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

    /// <summary>提取帖子 URL 中的 postId(https://www.kurobbs.com/mc/post/{id})。</summary>
    private static string ExtractPostId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }
        var m = PostIdRegex().Match(url);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>提取公告富文本 content 中的首张图片(http 绝对地址才采用,否则回退占位图)。</summary>
    private static string ExtractFirstImage(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        var m = FirstImgSrcRegex().Match(html);
        var url = m.Success ? m.Groups[1].Value.Trim() : "";
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? url
            : "";
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
            html = HtmlTagRegex().Replace(html, " ");
        }
        var text = WebUtility.HtmlDecode(html);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length > 60 ? text[..60] + "…" : text;
    }

    // 字面量正则统一源生成:AOT 下真预编译,免运行时缓存字典查找(公告列表每次加载逐条求值)。
    [System.Text.RegularExpressions.GeneratedRegex(@"mc/post/(\d+)")]
    private static partial System.Text.RegularExpressions.Regex PostIdRegex();

    [System.Text.RegularExpressions.GeneratedRegex("<img[^>]+?src=[\"']([^\"']+)[\"']", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex FirstImgSrcRegex();

    [System.Text.RegularExpressions.GeneratedRegex("<[^>]+>")]
    private static partial System.Text.RegularExpressions.Regex HtmlTagRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex WhitespaceRegex();
}
