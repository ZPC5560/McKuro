using McKuro.Core.Models.User;

namespace McKuro.Core.Services.User;

/// <summary>
/// 日常清单单项(由 <see cref="DailyChecklistFactory"/> 从角色每日数据推导的纯数据快照)。
/// </summary>
/// <param name="Key">稳定标识(测试/定位用,如 GameSign/Liveness/Energy)。</param>
/// <param name="Title">显示名。</param>
/// <param name="Detail">进度或状态文本(如 "80/100"、"已签到"、"—")。</param>
/// <param name="IsDone">任务是否已完成(资源项恒 false,不参与完成判定)。</param>
/// <param name="IsExecutable">McKuro 可代为执行(游戏签到/库街区任务)。</param>
/// <param name="IsResource">资源型条目(体力/结晶单质):显示进度条而非完成状态。</param>
/// <param name="Cur">当前值(无数据为 0)。</param>
/// <param name="Total">总量(0 表示未知,不显示进度)。</param>
public sealed record DailyChecklistEntry(
    string Key,
    string Title,
    string Detail,
    bool IsDone,
    bool IsExecutable,
    bool IsResource,
    int Cur,
    int Total)
{
    /// <summary>是否显示进度条(资源型且有总量)。</summary>
    public bool HasProgress => IsResource && Total > 0;

    /// <summary>进度 0-100(无总量恒 0)。</summary>
    public double Percent => Total > 0 ? Math.Clamp(Cur * 100.0 / Total, 0, 100) : 0;

    /// <summary>单行摘要文本(完成项加 "✓ " 前缀,如 "✓ 游戏签到 已签到"、"每日活跃度 80/100")。</summary>
    public string SummaryText
    {
        get
        {
            var body = string.IsNullOrEmpty(Detail) || Detail == "—" ? Title : $"{Title} {Detail}";
            return IsDone ? $"✓ {body}" : body;
        }
    }
}

/// <summary>
/// 今日日常清单工厂:把数据中心每日数据(RoleDailyData)翻译成统一的任务/资源清单。
/// <para>判定规则:任务项 total&gt;0 且 cur&gt;=total 记完成;total=0 无法判定,记未完成并显示当前值。
/// 体力/结晶单质为资源型,仅展示进度,不做完成判定。</para>
/// </summary>
public static class DailyChecklistFactory
{
    /// <summary>
    /// 构建清单。
    /// </summary>
    /// <param name="data">数据中心每日数据(null 表示拉取失败,返回空清单)。</param>
    /// <param name="bbsGoldCur">库街区今日已获库洛币(&lt;0 表示未知,不显示数值)。</param>
    /// <param name="bbsGoldMax">库街区每日库洛币上限(&lt;=0 表示未知,不显示数值)。</param>
    public static List<DailyChecklistEntry> Build(RoleDailyData? data, int bbsGoldCur = -1, int bbsGoldMax = -1)
    {
        if (data is null)
        {
            return [];
        }

        var list = new List<DailyChecklistEntry>
        {
            Task("GameSign", "游戏签到", data.HasSignIn ? "已签到" : "未签到", data.HasSignIn, isExecutable: true),
        };

        if (bbsGoldMax > 0)
        {
            list.Add(Task(
                "KuroBbsTasks", "库街区任务",
                $"库洛币 {bbsGoldCur}/{bbsGoldMax}",
                bbsGoldCur >= bbsGoldMax, isExecutable: true));
        }
        else
        {
            list.Add(Task("KuroBbsTasks", "库街区任务", "—", isDone: false, isExecutable: true));
        }

        var liveness = Progress(data.LivenessData, data.LivenessLimit);
        list.Add(Task("Liveness", "每日活跃度", liveness.Detail, liveness.Done));

        var weekly = Progress(data.WeeklyData, data.WeeklyLimit);
        list.Add(Task("Weekly", "周本·战歌重奏", weekly.Detail, weekly.Done));

        var newTower = Progress(data.NewTowerData);
        list.Add(Task("NewTower", "终焉矩阵", newTower.Detail, newTower.Done));

        var slash = Progress(data.SlashTowerData);
        list.Add(Task("SlashTower", "冥歌海墟", slash.Detail, slash.Done));

        var rouge = Progress(data.RougeData);
        list.Add(Task("Rouge", "千道门扉的异想", rouge.Detail, rouge.Done));

        var frame = Progress(data.WeeklyFrameData);
        list.Add(Task("WeeklyFrame", "周度游历", frame.Detail, frame.Done));

        var energy = Progress(data.EnergyData);
        list.Add(Resource("Energy", "体力·结晶波片", energy.Detail, energy.Cur, energy.Total));

        var store = Progress(data.StoreEnergyData);
        list.Add(Resource("StoreEnergy", "结晶单质", store.Detail, store.Cur, store.Total));

        return list;
    }

    private static DailyChecklistEntry Task(string key, string title, string detail, bool isDone, bool isExecutable = false) =>
        new(key, title, detail, isDone, isExecutable, IsResource: false, Cur: 0, Total: 0);

    private static DailyChecklistEntry Resource(string key, string title, string detail, int cur, int total) =>
        new(key, title, detail, IsDone: false, IsExecutable: false, IsResource: true, cur, total);

    private static (int Cur, int Total, string Detail, bool Done) Progress(RoleDailyDetail? detail, int fallbackTotal = 0)
    {
        var cur = detail?.Cur ?? 0;
        var total = detail is { Total: > 0 } ? detail.Total : fallbackTotal;
        var text = total > 0 ? $"{cur}/{total}" : cur > 0 ? $"{cur}" : "—";
        return (cur, total, text, total > 0 && cur >= total);
    }
}
