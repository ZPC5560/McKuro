using System;
using System.Runtime.InteropServices;

namespace Sparkle.Live2DView;

/// <summary>
/// Cubism Core 兼容层(shim)的可选扩展导出。
///
/// <para>
/// <b>背景</b>:macOS/Linux 的 <c>libLive2DCubismCore</c> 是项目自建的桥接层
/// (见 <c>Assets/live2d/shim/shim.c</c>),它把 Cubism 4 的
/// <c>csmGetDrawableRenderOrders</c> 转发到 Cubism 5 的 <c>csmGetRenderOrders</c>,
/// 并为每个模型缓存一块「稳定的」renderOrder 缓冲(调用方的写入可跨 <c>csmUpdateModel</c> 保留)。
/// </para>
///
/// <para>
/// <b>为什么需要本文件</b>:那块缓存以 <c>csmModel*</c> 为键且不会自己失效。模型销毁后
/// 分配器会复用同一地址,新模型会命中旧缓存并直接拿到上一个模型的 renderOrder
/// (图层顺序错乱)。故模型释放时通知 shim 丢弃该条目。
/// </para>
///
/// <para>
/// <b>兼容性</b>:Windows 用的是自带 <c>csmGetDrawableRenderOrders</c> 的定制 Core,
/// 不导出本扩展。此时首次调用会抛 <see cref="EntryPointNotFoundException"/>,
/// 本类据此永久降级为 no-op(只付一次异常代价),不影响渲染。
/// 同理,若随包的 shim 二进制尚未按最新 <c>shim.c</c> 重建,也只是降级为 no-op。
/// </para>
/// </summary>
internal static class Live2DCoreShim
{
    /// <summary>与 SDK 的 DllImport 一致:Core 的库名(解析由 <see cref="CubismRuntime"/> 注册)。</summary>
    private const string CoreLibraryName = "Live2DCubismCore";

    /// <summary>该导出不可用(Windows 定制 Core / 旧 shim 二进制):不再重试。</summary>
    private static bool _unavailable;

    [DllImport(CoreLibraryName, EntryPoint = "mckuroForgetRenderOrderCache", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ForgetRenderOrderCacheCore(IntPtr model);

    /// <summary>
    /// 通知 shim 丢弃指定模型的 renderOrder 缓存(模型销毁后调用)。
    /// 任何失败都静默降级 —— 这只影响缓存正确性,绝不能让释放路径抛异常。
    /// </summary>
    /// <param name="model">Cubism 原生 <c>csmModel*</c>;为 <see cref="IntPtr.Zero"/> 时直接跳过。</param>
    public static void ForgetRenderOrderCache(IntPtr model)
    {
        if (_unavailable || model == IntPtr.Zero)
        {
            return;
        }

        try
        {
            ForgetRenderOrderCacheCore(model);
        }
        catch (EntryPointNotFoundException)
        {
            // Windows 定制 Core 或不含该导出的旧 shim:静默降级
            _unavailable = true;
        }
        catch (DllNotFoundException)
        {
            _unavailable = true;
        }
        catch (Exception)
        {
            // 其余异常同样不得影响模型释放
        }
    }
}
