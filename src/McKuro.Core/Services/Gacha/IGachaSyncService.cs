using McKuro.Core.Models.Gacha;

namespace McKuro.Core.Services.Gacha;

/// <summary>抽卡同步服务接口。</summary>
public interface IGachaSyncService
{
    Task<GachaSyncResult> SyncFromLocalLogAsync(IUpPoolProvider? upPoolProvider = null, CancellationToken ct = default);

    Task<GachaSyncResult> SyncAsync(GachaRecordRequest request, IUpPoolProvider? upPoolProvider = null, CancellationToken ct = default);
}
