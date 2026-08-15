using donet.Core.Models.Gacha;
using donet.Core.Services.Game;

namespace donet.Core.Services.Gacha;

/// <summary>同步结果状态。</summary>
public enum GachaSyncStatus
{
    Success,
    GameDirNotSet,
    ClientLogNotFound,
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
public sealed class GachaSyncService
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

    /// <summary>
    /// 从本地游戏日志同步抽卡记录并返回分析结果。
    /// </summary>
    public async Task<GachaSyncResult> SyncFromLocalLogAsync(
        IUpPoolProvider? upPoolProvider = null,
        CancellationToken ct = default)
    {
        var logFile = _pathResolver.ClientLogPath;
        if (logFile is null)
        {
            return new GachaSyncResult { Status = GachaSyncStatus.GameDirNotSet, Message = "未设置游戏目录" };
        }
        if (!File.Exists(logFile))
        {
            return new GachaSyncResult
            {
                Status = GachaSyncStatus.ClientLogNotFound,
                Message = $"未找到日志文件: {logFile}",
            };
        }

        var decrypted = ClientLogDecryptor.DecryptFile(logFile);
        var url = GachaUrlExtractor.FindRecordUrl(decrypted);
        if (url is null)
        {
            return new GachaSyncResult
            {
                Status = GachaSyncStatus.RecordUrlNotFound,
                Message = "日志中未找到抽卡记录链接(请先在游戏中打开一次抽卡记录页面)",
            };
        }

        var request = GachaUrlExtractor.ParseUrl(url);
        if (request is null)
        {
            return new GachaSyncResult
            {
                Status = GachaSyncStatus.InvalidRequestParams,
                Message = "抽卡记录链接参数不完整",
            };
        }

        return await SyncAsync(request, upPoolProvider, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用解析出的请求参数同步抽卡记录。
    /// </summary>
    public async Task<GachaSyncResult> SyncAsync(
        GachaRecordRequest request,
        IUpPoolProvider? upPoolProvider = null,
        CancellationToken ct = default)
    {
        try
        {
            var all = await _api.QueryAllAsync(request, ct).ConfigureAwait(false);

            int newRecords = 0;
            int total = 0;
            foreach (var (type, records) in all)
            {
                newRecords += _store.UpsertRecords(request.PlayerId, records);
                total += records.Count;
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
