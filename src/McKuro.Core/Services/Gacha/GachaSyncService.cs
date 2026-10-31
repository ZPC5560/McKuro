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

    /// <summary>
    /// 日志文件存在但读不出来(权限不足 / IO 错误 / 读到空文件)。
    /// 与 <see cref="LogKeyMismatch"/> 不同:换个时机重试或检查权限即可,不需要升级软件。
    /// </summary>
    ClientLogReadFailed,
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

        // 读文件 / 逐字节解密 / UTF-8 校验 / 正则扫描放到线程池:日志可达数十 MB,
        // 而本方法在第一个 await 之前的部分跑在调用方(UI)线程上。
        var (failure, request) = await Task.Run(() => ReadRequestFromLog(logFile), ct).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        return await SyncAsync(request!, upPoolProvider, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从本地日志解析出抽卡请求参数(同步、CPU/IO 密集,由 <see cref="SyncFromLocalLogAsync"/> 放到线程池执行)。
    /// 成功时返回 (null, request);失败时返回 (失败结果, null)。
    /// </summary>
    private static (GachaSyncResult? Failure, GachaRecordRequest? Request) ReadRequestFromLog(string logFile)
    {
        // 解密并校验:密钥不匹配时逐字节异或不会报错,只会产出乱码,
        // 所以必须靠 DecryptFileChecked 把"密钥变了"和"没搜到链接"分开(两者提示完全不同)。
        var decrypted = ClientLogDecryptor.DecryptFileChecked(logFile);
        if (!decrypted.IsSuccess)
        {
            // 只有 KeyMismatch 才提示"请升级 McKuro";文件不存在 / 读不出来各给各的提示,
            // 否则游戏运行中同步(最常见的用法)遇到共享冲突会被误报成"加密方式已变更"。
            var failure = decrypted.Status switch
            {
                ClientLogDecryptStatus.FileMissing => new GachaSyncResult
                {
                    Status = GachaSyncStatus.ClientLogNotFound,
                    Message = CoreStrings.F("Core.Gacha.LogNotFound", $"未找到日志文件: {logFile}", logFile),
                },
                ClientLogDecryptStatus.ReadFailed => new GachaSyncResult
                {
                    Status = GachaSyncStatus.ClientLogReadFailed,
                    Message = CoreStrings.F("Core.Gacha.LogReadFailed", $"无法读取日志文件(可能权限不足或文件被锁定),请稍后重试: {logFile}", logFile),
                },
                _ => new GachaSyncResult
                {
                    Status = GachaSyncStatus.LogKeyMismatch,
                    Message = CoreStrings.T(
                        "Core.Gacha.LogKeyMismatch",
                        "游戏日志解密失败:游戏可能更新了日志加密方式,当前版本的 McKuro 暂时解不开。"
                        + "请升级到最新版本;在此期间可改用「云鸣潮」通道同步"),
                },
            };
            return (failure, null);
        }

        var url = GachaUrlExtractor.FindRecordUrl(decrypted.Text!);
        if (url is null)
        {
            return (new GachaSyncResult
            {
                Status = GachaSyncStatus.RecordUrlNotFound,
                Message = CoreStrings.T("Core.Gacha.NoGachaUrl", "日志中未找到抽卡记录链接(请先在游戏中打开一次抽卡记录页面)"),
            }, null);
        }

        var request = GachaUrlExtractor.ParseUrl(url);
        if (request is null)
        {
            return (new GachaSyncResult
            {
                Status = GachaSyncStatus.InvalidRequestParams,
                Message = CoreStrings.T("Core.Gacha.GachaUrlIncomplete", "抽卡记录链接参数不完整"),
            }, null);
        }

        return (null, request);
    }

    /// <inheritdoc/>
    public async Task<GachaSyncResult> SyncAsync(
        GachaRecordRequest request,
        IUpPoolProvider? upPoolProvider = null,
        CancellationToken ct = default)
    {
        try
        {
            int total = 0;
            int succeededPools = 0;
            string? lastError = null;
            // 新增数 = 同步后总数 - 同步前总数(按池覆盖写入,写入行数不等于新增数)
            int countBefore = _store.CountRecords(request.PlayerId);

            // 按池查询:成功即"直接覆盖"(先删后插,含空池清空旧数据),避免残留/累加;
            // 仅查询失败(网络/限流等)时保留旧缓存避免丢历史。
            foreach (var poolType in CardPoolTypeValues.All)
            {
                IReadOnlyList<GachaRecord> records;
                try
                {
                    records = await _api.QueryAsync(request, poolType, ct).ConfigureAwait(false);
                }
                catch (GachaApiException ex)
                {
                    // 该池查询失败(网络/限流等):保留旧缓存,不删除已有记录
                    lastError = ex.Message;
                    continue;
                }

                succeededPools++;
                total += records.Count;
                // 删和插必须用同一个卡池(查询用的池号),且在同一事务内:
                // 按记录推导的 PoolType 会把角色常驻池里的武器写进武器常驻池,随后又被池 4 的删除抹掉。
                _store.ReplacePlayerPool(request.PlayerId, poolType, records);
            }

            // 所有卡池都失败(典型:抽卡链接 record_id 过期):不能报"同步成功 0 条",把服务端提示带给用户。
            if (succeededPools == 0 && lastError is not null)
            {
                return new GachaSyncResult { Status = GachaSyncStatus.ApiError, Message = lastError, Request = request };
            }

            int newRecords = Math.Max(0, _store.CountRecords(request.PlayerId) - countBefore);

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
