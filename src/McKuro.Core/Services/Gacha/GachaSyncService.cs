using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.CloudGame;
using McKuro.Core.Services.Game;

namespace McKuro.Core.Services.Gacha;

/// <summary>同步结果状态。</summary>
public enum GachaSyncStatus
{
    Success,
    GameDirNotSet,
    ClientLogNotFound,

    /// <summary>
    /// 日志解密校验失败:解出来不是合法 UTF-8 明文日志。
    /// <para>
    /// 与 <see cref="RecordUrlNotFound"/> 是两件完全不同的事:这里是**游戏换了日志加密密钥**,
    /// 本版本的算法解不开,用户做什么都没用,只能等软件更新;而后者是日志本来就是好的、
    /// 只是里面还没有抽卡记录(用户没在游戏里打开过抽卡页面)。
    /// 早先两者混为一谈,报了后者,导致用户被指引去做一件注定无效的事。
    /// </para>
    /// </summary>
    LogKeyMismatch,

    RecordUrlNotFound,
    InvalidRequestParams,
    ApiError,
}

/// <summary>抽卡同步结果。</summary>
public sealed class GachaSyncResult
{
    public required GachaSyncStatus Status { get; init; }
    public string? Message { get; init; }
    public GachaRecordRequest? Request { get; init; }
    public int NewRecords { get; init; }
    public int TotalRecords { get; init; }
    public GachaAnalysisResult? Analysis { get; init; }

    public bool IsSuccess => Status == GachaSyncStatus.Success;
}

/// <summary>
/// 抽卡同步编排服务:
/// 本地 Client.log → 解密 → 提取记录链接 → 解析参数 → 查询官方接口 → 合并入库 → 分析。
/// </summary>
public sealed class GachaSyncService : IGachaSyncService
{
    private readonly GachaApiClient _api;
    private readonly GachaRecordStore _store;
    private readonly GamePathResolver _pathResolver;

    public GachaSyncService(GachaApiClient api, GachaRecordStore store, GamePathResolver pathResolver)
    {
        _api = api;
        _store = store;
        _pathResolver = pathResolver;
    }

    /// <inheritdoc/>
    public async Task<GachaSyncResult> SyncFromLocalLogAsync(
        IUpPoolProvider? upPoolProvider = null,
        CancellationToken ct = default)
    {
        var logFile = _pathResolver.ClientLogPath;
        if (logFile is null)
        {
            return new GachaSyncResult { Status = GachaSyncStatus.GameDirNotSet, Message = CoreStrings.T("Core.Gacha.NoGameDir", "未设置游戏目录") };
        }
        if (!File.Exists(logFile))
        {
            return new GachaSyncResult
            {
                Status = GachaSyncStatus.ClientLogNotFound,
                Message = CoreStrings.F("Core.Gacha.LogNotFound", $"未找到日志文件: {logFile}", logFile),
            };
        }

        // 解密并校验:密钥不匹配时逐字节异或不会报错,只会产出乱码,
        // 所以必须靠 DecryptFileChecked 把"密钥变了"和"没搜到链接"分开(两者提示完全不同)。
        var decrypted = ClientLogDecryptor.DecryptFileChecked(logFile);
        if (!decrypted.IsSuccess)
        {
            return new GachaSyncResult
            {
                Status = GachaSyncStatus.LogKeyMismatch,
                Message = CoreStrings.T(
                    "Core.Gacha.LogKeyMismatch",
                    "游戏日志解密失败:游戏可能更新了日志加密方式,当前版本的 McKuro 暂时解不开。"
                    + "请升级到最新版本;在此期间可改用「云鸣潮」通道同步"),
            };
        }

        var url = GachaUrlExtractor.FindRecordUrl(decrypted.Text);
        if (url is null)
        {
            return new GachaSyncResult
            {
                Status = GachaSyncStatus.RecordUrlNotFound,
                Message = CoreStrings.T("Core.Gacha.NoGachaUrl", "日志中未找到抽卡记录链接(请先在游戏中打开一次抽卡记录页面)"),
            };
        }

        var request = GachaUrlExtractor.ParseUrl(url);
        if (request is null)
        {
            return new GachaSyncResult
            {
                Status = GachaSyncStatus.InvalidRequestParams,
                Message = CoreStrings.T("Core.Gacha.GachaUrlIncomplete", "抽卡记录链接参数不完整"),
            };
        }

        return await SyncAsync(request, upPoolProvider, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<GachaSyncResult> SyncAsync(
        GachaRecordRequest request,
        IUpPoolProvider? upPoolProvider = null,
        CancellationToken ct = default)
    {
        try
        {
            int newRecords = 0;
            int total = 0;

            // 按池查询:成功即"直接覆盖"(先删后插,含空池清空旧数据),避免残留/累加;
            // 仅查询失败(网络/限流等)时保留旧缓存避免丢历史。
            foreach (var poolType in CardPoolTypeValues.All)
            {
                IReadOnlyList<GachaRecord> records;
                try
                {
                    records = await _api.QueryAsync(request, poolType, ct).ConfigureAwait(false);
                }
                catch (GachaApiException)
                {
                    // 该池查询失败(网络/限流等):保留旧缓存,不删除已有记录
                    continue;
                }

                total += records.Count;
                _store.DeletePlayerPool(request.PlayerId, poolType);
                if (records.Count > 0)
                {
                    newRecords += _store.InsertRecords(request.PlayerId, records);
                }
            }

            var stored = _store.GetRecords(request.PlayerId);
            IReadOnlyDictionary<CardPoolType, HashSet<int>>? upIds = null;
            if (upPoolProvider is not null)
            {
                upIds = await upPoolProvider.GetUpIdsAsync(ct).ConfigureAwait(false);
            }

            var analysis = new GachaAnalysisService().Analyze(request.PlayerId, stored, upIds);
            return new GachaSyncResult
            {
                Status = GachaSyncStatus.Success,
                Request = request,
                NewRecords = newRecords,
                TotalRecords = total,
                Analysis = analysis,
            };
        }
        catch (GachaApiException ex)
        {
            return new GachaSyncResult { Status = GachaSyncStatus.ApiError, Message = ex.Message };
        }
        catch (Exception ex)
        {
            return new GachaSyncResult { Status = GachaSyncStatus.ApiError, Message = ex.Message };
        }
    }
}
