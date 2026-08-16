using McKuro.Core.Models.Kuro;
using McKuro.Core.Services.Settings;

namespace McKuro.Core.Services.Kuro;

/// <summary>库街区账号管理(登录态持久化)。</summary>
public sealed class KuroAccountService
{
    private readonly SettingsService _settings;

    public KuroAccountService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>所有已保存账号。</summary>
    public IReadOnlyList<KuroAccount> GetAccounts() => _settings.Current.KuroAccounts;

    /// <summary>当前账号(登录态);DeviceId 自动统一为稳定设备 ID。</summary>
    public KuroAccount? Current
    {
        get
        {
            var id = _settings.Current.CurrentKuroUserId;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            var account = _settings.Current.KuroAccounts.FirstOrDefault(a => a.UserId == id);
            if (account is not null)
            {
                EnsureStableDeviceId(account);
            }
            return account;
        }
        set
        {
            _settings.Current.CurrentKuroUserId = value?.UserId ?? "";
            _settings.Save();
        }
    }

    /// <summary>添加或更新账号(DeviceId 统一为稳定设备 ID)。</summary>
    public void AddOrUpdate(KuroAccount account)
    {
        EnsureStableDeviceId(account);
        var accounts = _settings.Current.KuroAccounts;
        var index = accounts.FindIndex(a => a.UserId == account.UserId);
        if (index >= 0)
        {
            accounts[index] = account;
        }
        else
        {
            accounts.Add(account);
        }
        _settings.Current.CurrentKuroUserId = account.UserId;
        _settings.Save();
    }

    /// <summary>确保账号 DeviceId 为稳定设备 ID(空/变化时统一,避免 did 不稳定触发极验风控)。</summary>
    private void EnsureStableDeviceId(KuroAccount account)
    {
        var stable = _settings.Current.StableDeviceId;
        if (string.IsNullOrEmpty(stable))
        {
            stable = KuroClient.NewDeviceId();
            _settings.Current.StableDeviceId = stable;
            _settings.Save();
        }
        if (string.IsNullOrEmpty(account.DeviceId) || !string.Equals(account.DeviceId, stable, StringComparison.Ordinal))
        {
            account.DeviceId = stable;
            _settings.Save();
        }
    }

    /// <summary>移除账号。</summary>
    public void Remove(string userId)
    {
        var accounts = _settings.Current.KuroAccounts;
        accounts.RemoveAll(a => a.UserId == userId);
        if (_settings.Current.CurrentKuroUserId == userId)
        {
            _settings.Current.CurrentKuroUserId = "";
        }
        _settings.Save();
    }
}
