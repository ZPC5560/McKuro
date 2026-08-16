using McKuro.Core.Models.Kuro;

namespace McKuro.Core.Services.Settings;

/// <summary>
/// 应用设置服务接口(便于 DI 注入与单元测试)。
/// </summary>
public interface ISettingsService
{
    /// <summary>当前内存中的设置实例(直接修改字段不会立即落盘,需调用 <see cref="Save"/>)。</summary>
    AppSettings Current { get; }

    /// <summary>同步保存到磁盘(原子:临时文件 + File.Move)。</summary>
    void Save();

    /// <summary>异步保存(AOT 兼容;I/O 绑定到线程池)。</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>重新从磁盘加载并替换当前设置(异常时保留内存实例)。</summary>
    void Reload();
}
