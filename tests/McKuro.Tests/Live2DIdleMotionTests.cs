using Sparkle.Live2DView;

namespace McKuro.Tests;

/// <summary>
/// <see cref="Live2DIdleMotion"/>:程序化待机(呼吸/轻摆)的归一化偏移生成。
///
/// 这不是"锦上添花":第三方导出模型的参数名不是官方命名,SDK 写死的呼吸/视线跟随全部落空,
/// 只剩 motion3.json 的几条肢体曲线,实机看起来就是静止的。这里生成的偏移是让模型"活着"的主通道。
/// </summary>
public class Live2DIdleMotionTests
{
    /// <summary>约定:偏移必须落在 ±1 内,调用方按半量程缩放后才不会越过参数上下限。</summary>
    [Fact]
    public void 所有偏移始终在正负一之内()
    {
        var motion = new Live2DIdleMotion();

        for (int i = 0; i < 20000; i++)
        {
            foreach (Live2DIdleMotion.Offset offset in motion.Advance(1.0 / 60))
            {
                Assert.InRange(offset.Normalized, -1f, 1f);
            }
        }
    }

    /// <summary>长达 10 分钟的推进里时间必须单调增长 —— 否则动画会倒退或卡住。</summary>
    [Fact]
    public void 时间单调增长()
    {
        var motion = new Live2DIdleMotion();
        double previous = motion.Seconds;

        for (int i = 0; i < 36000; i++)
        {
            motion.Advance(1.0 / 60);
            Assert.True(motion.Seconds > previous, $"第 {i} 帧时间未增长");
            previous = motion.Seconds;
        }

        Assert.InRange(motion.Seconds, 599.0, 601.0);
    }

    /// <summary>非法/缺失的时间增量不能污染累计时间(否则一次异常会让动画永久跳变)。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void 非法时间增量被忽略(double delta)
    {
        var motion = new Live2DIdleMotion();

        motion.Advance(delta);

        Assert.Equal(0.0, motion.Seconds);
    }

    /// <summary>呼吸等角色必须真的在动 —— 这正是"静止感"要解决的问题。</summary>
    [Fact]
    public void 每个角色都产生可观测的摆动()
    {
        var motion = new Live2DIdleMotion();
        var seen = new Dictionary<Live2DSemanticParameter, (float Min, float Max)>();

        for (int i = 0; i < 60 * 60; i++)
        {
            foreach (Live2DIdleMotion.Offset offset in motion.Advance(1.0 / 60))
            {
                if (seen.TryGetValue(offset.Role, out (float Min, float Max) range))
                {
                    seen[offset.Role] = (Math.Min(range.Min, offset.Normalized), Math.Max(range.Max, offset.Normalized));
                }
                else
                {
                    seen[offset.Role] = (offset.Normalized, offset.Normalized);
                }
            }
        }

        Assert.NotEmpty(seen);
        foreach ((Live2DSemanticParameter role, (float min, float max)) in seen)
        {
            // 峰值幅度 > 0.1 才算"肉眼可见";周期最长 15.3s,60s 内必然跑完一整轮。
            Assert.True(max - min > 0.2f, $"角色 {role} 在 60 秒内摆幅只有 {max - min:0.###}");
        }
    }

    /// <summary>每一帧都要输出全部待机角色,缺失会让某些部位停止摆动。</summary>
    [Fact]
    public void 每帧输出全部待机角色()
    {
        var motion = new Live2DIdleMotion();

        IReadOnlyList<Live2DIdleMotion.Offset> first = motion.Advance(1.0 / 60);
        int count = first.Count;

        Assert.Equal(Live2DIdleMotion.Waves.Count, count);
        Assert.True(count >= 6, $"只输出 {count} 个角色");

        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(count, motion.Advance(1.0 / 60).Count);
        }

        Assert.Equal(count, first.Select(o => o.Role).Distinct().Count());
    }

    /// <summary>
    /// <c>gaze_x</c>/<c>gaze_y</c> 在第三方导出模型里是<b>死参数</b>(满量程位移为 0),
    /// 写它们纯属浪费且会掩盖真正的驱动。待机波表里不能出现。
    /// </summary>
    [Fact]
    public void 待机不包含死参数()
    {
        var roles = Live2DIdleMotion.Waves.Select(w => w.Role).ToArray();

        Assert.DoesNotContain(Live2DSemanticParameter.GazeX, roles);
        Assert.DoesNotContain(Live2DSemanticParameter.GazeY, roles);
    }

    /// <summary>
    /// 周期与振幅的标定:第三方导出把参数烘焙成局部形变,满量程也只移动几个屏幕像素,
    /// 因此这些角色必须给到足够振幅,否则在屏幕上根本看不出来。
    /// </summary>
    [Fact]
    public void 关键角色使用足够大的振幅()
    {
        var motion = new Live2DIdleMotion();
        var peak = new Dictionary<Live2DSemanticParameter, float>();

        for (int i = 0; i < 60 * 30; i++)
        {
            foreach (Live2DIdleMotion.Offset offset in motion.Advance(1.0 / 60))
            {
                float abs = Math.Abs(offset.Normalized);
                peak[offset.Role] = peak.TryGetValue(offset.Role, out float current)
                    ? Math.Max(current, abs)
                    : abs;
            }
        }

        Assert.True(peak[Live2DSemanticParameter.HeadRoll] > 0.2f, "Head Roll 峰值过小");
        Assert.True(peak[Live2DSemanticParameter.HeadPitch] > 0.2f, "Head Pitch 峰值过小");
        Assert.True(peak[Live2DSemanticParameter.BodySway] > 0.3f, "Body Sway 峰值过小");
        Assert.True(peak[Live2DSemanticParameter.BodyRoll] > 0.25f, "Body Roll 峰值过小");
        Assert.True(peak[Live2DSemanticParameter.Breath] > 0.4f, "Breath 峰值过小");
    }

    /// <summary>
    /// 待机与指针跟随共用同一批参数,两者相加必须留有余量 ——
    /// 相加超过 ±1 会被 <c>SetParameterValue</c> 钳在边界,表现为"往某方向跟不过去"。
    /// 这条约束容易被后续调参破坏,必须锁住。
    /// </summary>
    [Fact]
    public void 待机与跟随之和不超过满量程()
    {
        foreach (Live2DIdleMotion.Wave wave in Live2DIdleMotion.Waves)
        {
            float follow = 0f;
            foreach (Live2DPointerFollow.Gain gain in Live2DPointerFollow.Gains)
            {
                if (gain.Role == wave.Role)
                {
                    follow = Math.Max(Math.Abs(gain.LookX), Math.Abs(gain.LookY));
                    break;
                }
            }

            float total = wave.Amplitude + follow;
            Assert.True(
                total <= 1.0f + 1e-6f,
                $"角色 {wave.Role} 待机 {wave.Amplitude} + 跟随 {follow} = {total} 超过满量程");
        }
    }

    /// <summary>最坏情况下(待机与跟随同向打满)合成值必须仍被钳在 ±1。</summary>
    [Fact]
    public void 合成值被钳制在正负一()
    {
        Assert.Equal(1f, Live2DPointerFollow.Combine(1f, 1f));
        Assert.Equal(-1f, Live2DPointerFollow.Combine(-1f, -1f));
        Assert.Equal(0.5f, Live2DPointerFollow.Combine(0.5f, 0f));
    }

    /// <summary>不同角色不能完全同步 —— 同相位会让整个模型像一块板在晃。</summary>
    [Fact]
    public void 角色之间不同步()
    {
        var motion = new Live2DIdleMotion();
        var samples = new Dictionary<Live2DSemanticParameter, List<float>>();

        for (int i = 0; i < 600; i++)
        {
            foreach (Live2DIdleMotion.Offset offset in motion.Advance(1.0 / 60))
            {
                if (!samples.TryGetValue(offset.Role, out List<float>? list))
                {
                    samples[offset.Role] = list = [];
                }

                list.Add(offset.Normalized);
            }
        }

        List<float> sway = samples[Live2DSemanticParameter.BodySway];
        List<float> yaw = samples[Live2DSemanticParameter.HeadYaw];

        double mean = sway.Average();
        double covariance = 0;
        double varianceA = 0;
        double varianceB = 0;
        for (int i = 0; i < sway.Count; i++)
        {
            double a = sway[i] - mean;
            double b = yaw[i] - yaw.Average();
            covariance += a * b;
            varianceA += a * a;
            varianceB += b * b;
        }

        double correlation = Math.Abs(covariance / Math.Sqrt(varianceA * varianceB));
        Assert.True(correlation < 0.95, $"Body Sway 与 Head Yaw 相关系数 {correlation:0.###}，过于同步");
    }

    /// <summary>确定性:同样的时间输入必须给出同样的输出(便于回归定位)。</summary>
    [Fact]
    public void 相同输入产生相同输出()
    {
        var a = new Live2DIdleMotion();
        var b = new Live2DIdleMotion();

        for (int i = 0; i < 1000; i++)
        {
            IReadOnlyList<Live2DIdleMotion.Offset> left = a.Advance(1.0 / 60);
            IReadOnlyList<Live2DIdleMotion.Offset> right = b.Advance(1.0 / 60);

            Assert.Equal(left.Count, right.Count);
            for (int k = 0; k < left.Count; k++)
            {
                Assert.Equal(left[k].Role, right[k].Role);
                Assert.Equal(left[k].Normalized, right[k].Normalized);
            }
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void 非法周期返回零(float period)
    {
        Assert.Equal(0f, Live2DIdleMotion.Oscillate(1.0, period, 0f));
    }

    [Fact]
    public void 振荡函数输出有界且确实振荡()
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < 10000; i++)
        {
            float value = Live2DIdleMotion.Oscillate(i * 0.01, 3.2f, 0.5f);
            Assert.InRange(value, -1f, 1f);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        Assert.True(max - min > 1.0f, $"振荡范围过小: {min:0.###}..{max:0.###}");
    }

    /// <summary>
    /// 标为「SDK 已驱动」的角色必须完全不再输出 —— 官方模型走 SDK 自带的呼吸/指针跟随,
    /// 若这里再写一遍,呼吸会翻倍、眨眼会被抢。
    /// </summary>
    [Fact]
    public void 交回SDK的角色不再输出()
    {
        var motion = new Live2DIdleMotion();
        motion.MarkHandledBySdk(Live2DParameterMap.AllRoles);

        for (int i = 0; i < 500; i++)
        {
            Assert.Empty(motion.Advance(1.0 / 60));
        }
    }

    /// <summary>只交回部分角色时,其余角色仍必须正常摆动。</summary>
    [Fact]
    public void 部分交回SDK时其余角色仍输出()
    {
        var motion = new Live2DIdleMotion();
        motion.MarkHandledBySdk([Live2DSemanticParameter.HeadYaw, Live2DSemanticParameter.HeadPitch]);

        IReadOnlyList<Live2DIdleMotion.Offset> offsets = motion.Advance(1.0 / 60);

        Assert.NotEmpty(offsets);
        Assert.DoesNotContain(offsets, o => o.Role == Live2DSemanticParameter.HeadYaw);
        Assert.DoesNotContain(offsets, o => o.Role == Live2DSemanticParameter.HeadPitch);
        Assert.Contains(offsets, o => o.Role == Live2DSemanticParameter.Breath);
    }

    /// <summary>重复标记必须整体覆盖,不能累加(切模型时会重新标注)。</summary>
    [Fact]
    public void 重复标记整体覆盖()
    {
        var motion = new Live2DIdleMotion();
        motion.MarkHandledBySdk(Live2DParameterMap.AllRoles);
        motion.MarkHandledBySdk([]);

        Assert.NotEmpty(motion.Advance(1.0 / 60));
    }
}
