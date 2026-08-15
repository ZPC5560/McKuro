using System.Text.RegularExpressions;
using donet.Core.Models.Gacha;

namespace donet.Core.Services.Gacha;

/// <summary>
/// 从解密后的客户端日志中提取抽卡记录链接,并解析为请求参数。
/// </summary>
public static partial class GachaUrlExtractor
{
    /// <summary>抽卡记录链接匹配(取日志中最后一次出现的链接)。</summary>
    [GeneratedRegex(@"https.*/aki/gacha/index\.html#/record[?=&A-Za-z0-9_\-%]+")]
    private static partial Regex RecordUrlRegex();

    public static string? FindRecordUrl(string? decryptedLog)
    {
        if (string.IsNullOrWhiteSpace(decryptedLog))
        {
            return null;
        }

        string? last = null;
        foreach (Match match in RecordUrlRegex().Matches(decryptedLog))
        {
            last = match.Value;
        }
        return last;
    }

    public static GachaRecordRequest? ParseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.Contains('?'))
        {
            return null;
        }

        var request = new GachaRecordRequest { RawUrl = url };
        try
        {
            var query = url[(url.IndexOf('?') + 1)..];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2)
                {
                    continue;
                }

                switch (kv[0])
                {
                    case "player_id":
                        request.PlayerId = kv[1];
                        break;
                    case "record_id":
                        request.RecordId = kv[1];
                        break;
                    case "resources_id":
                        request.CardPoolId = kv[1];
                        break;
                    case "gacha_type":
                        // gacha_type 即卡池类型
                        break;
                    case "svr_id":
                        request.ServerId = kv[1];
                        break;
                    case "lang":
                        request.Language = kv[1];
                        break;
                }
            }

            return request.IsValid ? request : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
