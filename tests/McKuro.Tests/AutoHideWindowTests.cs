using McKuro.Core.Services.Settings;
using McKuro.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Tests;

/// <summary>
/// 「沉浸 / 自动隐藏」两条链路的守卫:
/// <list type="number">
/// <item><see cref="EdgeRevealState"/>:贴边唤出、悬停保持、离开延迟收起的纯逻辑(窗口轮询每拍调用);</item>
/// <item>设置项的默认值与持久化(默认关闭,开启后重启仍生效)。</item>
/// </list>
/// </summary>
public class AutoHideWindowTests
{
    private static EdgeRevealState NewState() => new()
    {
        Extent = 76,
        RevealZone = 4,
        HoldZone = 8,
        HideDelayTicks = 3,
    };

    [Fact]
    public void Touching_Edge_Reveals_Immediately()
    {
        var state = NewState();
        Assert.False(state.Shown);

        Assert.True(state.Update(2)); // 贴边(≤4)
        Assert.True(state.Shown);
    }

    [Fact]
    public void Stays_Shown_While_Pointer_Hovers_The_Bar()
    {
        var state = NewState();
        state.ForceShow();

        // 展开区域 76 + 余量 8 = 84,期间连续喂入都不会收起
        Assert.True(state.Update(0));
        Assert.True(state.Update(50));
        Assert.True(state.Update(84));
        for (var i = 0; i < 20; i++)
        {
            Assert.True(state.Update(84));
        }
    }

    [Fact]
    public void Hides_Only_After_The_Configured_Delay()
    {
        var state = NewState();
        state.ForceShow();

        Assert.True(state.Update(400)); // 第 1 拍
        Assert.True(state.Update(400)); // 第 2 拍
        Assert.False(state.Update(400)); // 第 3 拍达到延迟 → 收起
        Assert.False(state.Shown);
    }

    [Fact]
    public void Hovering_Again_Resets_The_Hide_Countdown()
    {
        var state = NewState();
        state.ForceShow();

        Assert.True(state.Update(400));
        Assert.True(state.Update(400)); // 已累计 2 拍,还差 1 拍就要收起
        Assert.True(state.Update(50));  // 回到栏内 → 计时清零
        Assert.True(state.Update(400));
        Assert.True(state.Update(400));
        Assert.False(state.Update(400)); // 重新累计满 3 拍才收起
        Assert.False(state.Shown);
    }

    [Fact]
    public void Pointer_Outside_Window_Counts_As_Leaving()
    {
        var state = NewState();
        state.ForceShow();

        Assert.True(state.Update(null));
        Assert.True(state.Update(null));
        Assert.False(state.Update(null));
        Assert.False(state.Shown);
    }

    [Fact]
    public void Hidden_State_Never_Shows_Without_Touching_Edge()
    {
        var state = NewState();
        for (var i = 0; i < 30; i++)
        {
            Assert.False(state.Update(500));
        }
        Assert.False(state.Shown);
    }

    [Fact]
    public void Observe_Reveals_But_Never_Starts_The_Hide_Countdown()
    {
        var state = NewState();

        // 指针事件路径:贴边立即展开,并报告"发生了变化"
        Assert.True(state.Observe(3));
        Assert.True(state.Shown);

        // 高频事件离开贴边带:不得推进收起(否则几十次事件后就瞬间收起)
        for (var i = 0; i < 100; i++)
        {
            Assert.False(state.Observe(400));
        }
        Assert.True(state.Shown);

        // 收起仍由轮询 Update 推进
        Assert.True(state.Update(400));
        Assert.True(state.Update(400));
        Assert.False(state.Update(400));
    }

    [Fact]
    public void Observe_Keeps_Shown_While_Hovering_The_Bar()
    {
        var state = NewState();
        state.ForceShow();

        Assert.False(state.Observe(50)); // 已在展开区域:无状态变化,但保持展开
        Assert.True(state.Shown);
    }

    [Fact]
    public void Short_Hold_Zone_Releases_As_Soon_As_Pointer_Leaves_The_Bar_Area()
    {
        // 操作栏:高度 38 + 保持余量 20 = 58 DIP,2 拍(≈140ms)后收起
        var state = new EdgeRevealState
        {
            Extent = 38,
            RevealZone = 10,
            HoldZone = 20,
            HideDelayTicks = 2,
        };
        state.ForceShow();

        // 指针仍在栏内 / 紧贴栏下沿:保持
        Assert.True(state.Update(0));
        Assert.True(state.Update(38));
        Assert.True(state.Update(58));

        // 离开:两拍后就收起(旧值 8 拍 ≈560ms 是用户反馈的「有延迟」)
        Assert.True(state.Update(200));
        Assert.False(state.Update(200));
        Assert.False(state.Shown);
    }

    [Fact]
    public void Force_Show_And_Hide_Reset_Pending_Countdown()
    {
        var state = NewState();
        state.ForceShow();
        state.Update(400);
        state.Update(400); // 收起计时累计 2 拍

        state.ForceHide();
        Assert.False(state.Shown);
        // ForceHide 后即便继续在远处也保持收起
        Assert.False(state.Update(400));

        state.ForceShow();
        Assert.True(state.Shown);
        // ForceShow 清零计时:需要重新累计 3 拍才收起
        Assert.True(state.Update(400));
        Assert.True(state.Update(400));
        Assert.False(state.Update(400));
    }

    [Fact]
    public void Immersive_Settings_Default_Off_And_Survive_Reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "McKuro-immersive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new SettingsService(dir, NullLogger<SettingsService>.Instance);
            Assert.False(settings.Current.AutoHideNavRail);
            Assert.False(settings.Current.AutoHideTitleBar);

            settings.Current.AutoHideNavRail = true;
            settings.Current.AutoHideTitleBar = true;
            settings.Save();

            var reloaded = new SettingsService(dir, NullLogger<SettingsService>.Instance);
            Assert.True(reloaded.Current.AutoHideNavRail);
            Assert.True(reloaded.Current.AutoHideTitleBar);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
