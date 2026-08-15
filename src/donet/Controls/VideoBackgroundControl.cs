using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;

namespace donet.Controls;

/// <summary>
/// 背景封面控件:优先用 LibVLC 播放宣传视频,无可用 native 库或播放失败时回退到首帧静态图。
/// 依赖:系统装有 VLC(macOS/Linux)或应用目录带 VideoLAN.LibVLC native 库(Windows)。
/// 视频不可用不影响其余功能 —— 全程 try-catch,绝不抛出。
/// </summary>
public sealed class VideoBackgroundControl : Grid
{
    private static readonly Lazy<LibVLC?> LibVlc = new(TryCreateLibVlc);

    private MediaPlayer? _player;
    private VideoView? _videoView;
    private AsyncImage? _fallback;
    private bool _initialized;
    private bool _attached;

    public static readonly StyledProperty<string> VideoUrlProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, string>(nameof(VideoUrl));

    public static readonly StyledProperty<string> FallbackImageUrlProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, string>(nameof(FallbackImageUrl));

    public static readonly StyledProperty<bool> IsVideoEnabledProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, bool>(nameof(IsVideoEnabled));

    /// <summary>宣传视频 URL(空则不尝试播放)。</summary>
    public string VideoUrl
    {
        get => GetValue(VideoUrlProperty);
        set => SetValue(VideoUrlProperty, value);
    }

    /// <summary>首帧/静态图 URL(视频不可用时的回退封面)。</summary>
    public string FallbackImageUrl
    {
        get => GetValue(FallbackImageUrlProperty);
        set => SetValue(FallbackImageUrlProperty, value);
    }

    /// <summary>是否启用视频封面(用户设置;禁用则仅显示首帧图)。</summary>
    public bool IsVideoEnabled
    {
        get => GetValue(IsVideoEnabledProperty);
        set => SetValue(IsVideoEnabledProperty, value);
    }

    public VideoBackgroundControl()
    {
        ClipToBounds = true;
        _fallback = new AsyncImage
        {
            Stretch = Avalonia.Media.Stretch.UniformToFill,
        };
        Children.Add(_fallback);

        VideoUrlProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());
        FallbackImageUrlProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());
        IsVideoEnabledProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());

        DetachedFromVisualTree += (_, _) => DisposePlayer();
    }

    private static LibVLC? TryCreateLibVlc()
    {
        try
        {
            return new LibVLC();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LibVLC 初始化失败(将回退首帧图): {ex.Message}");
            return null;
        }
    }

    private void OnUrlChanged()
    {
        if (!_initialized)
        {
            // 等挂载后再初始化(需要平台句柄)
            AttachedToVisualTree += OnAttached;
            _initialized = true;
        }
        else if (_attached)
        {
            TryStartVideo();
        }
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttached;
        _attached = true;
        _fallback!.ImageUrl = FallbackImageUrl;
        TryStartVideo();
    }

    private void TryStartVideo()
    {
        DisposePlayer();

        if (!IsVideoEnabled || string.IsNullOrWhiteSpace(VideoUrl))
        {
            _fallback!.ImageUrl = FallbackImageUrl;
            return;
        }

        var libvlc = LibVlc.Value;
        if (libvlc is null)
        {
            // 无 native 库 → 回退首帧图
            _fallback!.ImageUrl = FallbackImageUrl;
            return;
        }

        try
        {
            _player = new MediaPlayer(libvlc)
            {
                EnableHardwareDecoding = true,
            };

            _videoView = new VideoView
            {
                MediaPlayer = _player,
                IsVisible = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            };
            Children.Add(_videoView);

            var media = new Media(libvlc, new Uri(VideoUrl));
            _player.Play(media);
            _player.EndReached += (_, _) =>
            {
                // 循环播放
                try
                {
                    _player.Time = 0;
                    _player.Play();
                }
                catch (Exception)
                {
                    // 忽略
                }
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"视频背景播放失败(回退首帧图): {ex.Message}");
            DisposePlayer();
            _fallback!.ImageUrl = FallbackImageUrl;
        }
    }

    private void DisposePlayer()
    {
        if (_videoView is not null)
        {
            Children.Remove(_videoView);
            _videoView = null;
        }

        if (_player is not null)
        {
            try
            {
                _player.Stop();
            }
            catch (Exception)
            {
                // 忽略
            }

            _player.Dispose();
            _player = null;
        }
    }
}
