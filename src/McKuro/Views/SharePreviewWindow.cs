using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Services;
using McKuro.ViewModels;

namespace McKuro.Views;

/// <summary>
/// 分享结果弹窗:分享图已生成,提供「复制图片 / 另存为 PNG / 关闭」功能键。
/// <para>
/// 纯代码构建(与项目其它对话框一致,避免额外 XAML/AOT 反射面)。<b>不做图片预览</b>:
/// 预览窗在 HiDPI 屏上的位图重采样发虚问题多轮修复未果,而复制/落盘的 PNG 本身是
/// 清晰的 —— 遂按用户要求移除预览,只保留功能键。
/// </para>
/// <para>
/// 曾实现过「发送到 QQ/微信/抖音」(复制 + 拉起客户端,聊天框 Ctrl+V 即发),
/// 因达不到"全自动发送"的预期被用户取消移除 —— 三个客户端均无公开的 Windows
/// 发送接口,不要再加回此功能。
/// </para>
/// <para>
/// 位图生命周期(评审反馈):分享图可达 ~190MB(48M 像素上限 × 4B),旧实现无人 Dispose,
/// 每次分享都泄漏一张 → 现在窗口 <see cref="Window.Closed"/> 时确定性释放;
/// 剪贴板走<b>克隆快照</b> —— Windows 剪贴板对位图是惰性读取(粘贴目标要时才序列化),
/// 直接把本图交给剪贴板会出现"关闭后粘贴失效"或"剪贴板提前释放导致另存失败"的
/// 所有权分叉,交给它一份独立克隆、本窗继续用原图,两侧都安全。
/// </para>
/// </summary>
public sealed class SharePreviewWindow : Window
{
    private readonly Bitmap _image;

    public SharePreviewWindow(Bitmap image, string title)
    {
        _image = image;

        Title = title;
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        // 关闭即释放分享图(ShowDialog 与 Show 两条路径都走 Closed;剪贴板持有的
        // 克隆与这里无关,由 GC 在剪贴板放引用后回收。Avalonia 的 Window 在 Close 时
        // 自行销毁原生对等体,托管侧无公开 Dispose 可调,大图资源就是这张位图)
        Closed += (_, _) =>
        {
            try
            {
                _image.Dispose();
            }
            catch (Exception)
            {
                // 释放失败不影响关闭
            }
        };

        // 副标题:像素尺寸(数字跨语言通用)
        var info = new TextBlock
        {
            Text = $"{image.PixelSize.Width} × {image.PixelSize.Height} px",
            FontSize = 12.5,
            Opacity = 0.75,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var copyButton = new Button { Content = LanguageService.Get("Roles.Share.CopyImage"), Padding = new Thickness(14, 7) };
        copyButton.Click += async (_, _) => await CopyAsync();
        var saveButton = new Button { Content = LanguageService.Get("Roles.Share.SaveAsPng"), Padding = new Thickness(14, 7) };
        saveButton.Click += async (_, _) => await SaveAsync();
        var closeButton = new Button { Content = LanguageService.Get("Roles.Share.Close"), Padding = new Thickness(14, 7) };
        closeButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20, 18),
            Spacing = 16,
            Children =
            {
                info,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { copyButton, saveButton, closeButton },
                },
            },
        };
    }

    /// <summary>复制图片到系统剪贴板(成功/失败都给悬浮提示;失败时仍可另存为 PNG)。</summary>
    private async Task CopyAsync()
    {
        try
        {
            var clipboard = Clipboard;
            if (clipboard is null)
            {
                Notify(LanguageService.Get("Roles.Share.CopyFailed"));
                return;
            }
            // 交给剪贴板的是克隆:它的释放时机(立即序列化 or 惰性读取后释放)不受本窗控制,
            // 隔离开后"复制 → 再另存"不会被已释放的位图背刺(评审反馈)。
            var payload = CloneForClipboard(_image) ?? _image;
            // Avalonia 12:IClipboard.SetDataAsync(IAsyncDataTransfer)。
            using var transfer = new Avalonia.Input.DataTransfer();
            transfer.Add(Avalonia.Input.DataTransferItem.Create(
                Avalonia.Input.DataFormat.Bitmap, payload));
            await clipboard.SetDataAsync(transfer);
            Notify(LanguageService.Get("Roles.Share.Copied"));
        }
        catch (Exception)
        {
            // 剪贴板不可用(被其它进程占用等):如实告知,引导改用另存为
            Notify(LanguageService.Get("Roles.Share.CopyFailed"));
        }
    }

    /// <summary>向主界面发一条悬浮提示(与项目其它页同一通道)。</summary>
    private static void Notify(string message)
        => WeakReferenceMessenger.Default.Send(new ShowToastMessage(message));

    /// <summary>
    /// 位图深拷贝(Bgra8888 画布 + <see cref="Bitmap.CopyPixels(ILockedFramebuffer)"/>,
    /// 格式/Alpha 转换交由 Avalonia;同 TintedAsyncImage 的成熟路径)。失败返回 null 由调用方兜底。
    /// </summary>
    private static Bitmap? CloneForClipboard(Bitmap source)
    {
        WriteableBitmap? target = null;
        var keep = false;
        try
        {
            target = new WriteableBitmap(source.PixelSize, source.Dpi,
                PixelFormats.Bgra8888, AlphaFormat.Premul);
            using var fb = target.Lock();
            source.CopyPixels(fb);
            keep = true;
            return target;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (!keep)
            {
                target?.Dispose();
            }
        }
    }

    /// <summary>另存为 PNG 文件。</summary>
    private async Task SaveAsync()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LanguageService.Get("Roles.Share.SaveDialogTitle"),
                SuggestedFileName = LanguageService.Format(
                    "Roles.Share.SuggestedFileName", DateTime.Now.ToString("yyyyMMdd-HHmmss")),
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
            });
            if (file is null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            // 注:Avalonia 12 的 Bitmap.Save(Stream, int?) 已标注过时,推荐重载的
            // BitmapEncoderOptions 位于 Avalonia.Skia(项目未直接引用),故保留旧重载并抑制警告。
#pragma warning disable CS0618
            _image.Save(stream);
#pragma warning restore CS0618
            Notify(LanguageService.Get("Roles.Share.Saved"));
        }
        catch (Exception ex)
        {
            // 保存失败如实提示用户(此前静默,用户会以为已保存成功)
            System.Console.Error.WriteLine($"[share] 保存 PNG 失败: {ex.Message}");
            Notify(LanguageService.Get("Roles.Share.SaveFailed"));
        }
    }
}
