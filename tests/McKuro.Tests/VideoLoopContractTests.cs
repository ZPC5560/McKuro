using System.Reflection;
using McKuro.Controls;

namespace McKuro.Tests;

/// <summary>
/// 背景视频「循环播放」契约:回归 <c>ShouldLoop</c> 被构造顺序吞掉的 bug。
/// <para>
/// <b>背景</b>:<c>VideoBackgroundControl.BackgroundMpvContext</c> 曾用
/// 「构造器给派生字段 <c>_loop</c> 赋值 + <see cref="HanumanInstitute.LibMpv.MpvContextBase"/>
/// 的虚方法 <c>OnPreInitialize()</c> 读该字段」的写法设置 <c>loop-file</c>。
/// 但基类构造器会在<b>派生类构造器体执行之前</b>调用 <c>OnPreInitialize()</c>,
/// 此时 <c>_loop</c> 仍是默认值 <c>false</c> → <c>loop-file</c> 恒被写成 <c>"no"</c>
/// → 背景封面视频播完一遍就停在末帧(用户报告的「视频卡住/不循环」;
/// 游戏退出、切换页面、久置后回到启动页都能看到)。
/// </para>
/// <para>
/// 本测试不加载 libmpv(CI 无该 native 库),而是用反射断言「表达循环语义的类型结构」:
/// 循环与否必须由<b>类型</b>决定(两个字面量子类),而不是靠构造器传进字段。
/// 这样即使日后有人重新引入「构造器传 bool」的写法,也会在这里失败。
/// </para>
/// </summary>
public sealed class VideoLoopContractTests
{
    private static readonly Type ControlType =
        typeof(VideoBackgroundControl);

    private static Type Nested(string name) =>
        ControlType.GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"VideoBackgroundControl 缺少嵌套类型 {name}(循环语义约定被改动了?)");

    /// <summary>
    /// 循环/一次性的两种行为必须由两个独立类型表达 ——
    /// 这是绕开「基类构造器先调虚方法、派生字段尚未赋值」的唯一可靠办法。
    /// </summary>
    [Fact]
    public void 循环与单次播放由两个独立上下文类型表达()
    {
        var looping = Nested("LoopingMpvContext");
        var oneShot = Nested("OneShotMpvContext");

        Assert.True(looping.IsClass && oneShot.IsClass);
        Assert.NotEqual(looping, oneShot);
    }

    /// <summary>
    /// 基类必须抽象:否则又可能出现「new BackgroundMpvContext(loop)」这种
    /// 把循环语义塞进构造器、最终被构造顺序吞掉的写法。
    /// </summary>
    [Fact]
    public void 上下文基类保持抽象以禁止构造器传参写法()
    {
        var baseType = Nested("BackgroundMpvContext");

        Assert.True(
            baseType.IsAbstract,
            "BackgroundMpvContext 必须保持抽象:它的选项值依赖基类构造期调用的 OnPreInitialize, "
            + "而该时机早于派生构造器体,实例字段尚未赋值 —— 若允许实例化(传 bool 进构造器), "
            + "loop-file 会被静默写成 \"no\",背景视频将不再循环。");
    }

    /// <summary>
    /// 两个子类都必须<b>各自声明</b>把 loop-file 写成字面常量的属性:
    /// 属性直接返回常量字符串,与构造顺序无关,正是修复的关键。
    /// <para>
    /// 断言必须落在「子类自己声明的 getter」上,而不是继承来的实现 ——
    /// 否则有人把逻辑挪回基类的字段模式(子类只传构造参数)时,
    /// 这里仍会沿继承链找到那个读字段的 getter 而误判通过。
    /// </para>
    /// </summary>
    [Fact]
    public void 两个子类分别以字面常量声明循环选项()
    {
        foreach (var (typeName, expected) in new[]
                 {
                     ("LoopingMpvContext", "inf"),
                     ("OneShotMpvContext", "no"),
                 })
        {
            var type = Nested(typeName);

            // 必须是本类型自己声明的属性(不能靠基类实现)
            var prop = type.GetProperty(
                "LoopFileOption",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                | BindingFlags.DeclaredOnly);

            Assert.True(
                prop is not null,
                $"{typeName} 必须自己声明 LoopFileOption 并直接返回字面量 \"{expected}\";"
                + "不得依赖基类实现(基类实现会在构造期读实例字段,被构造顺序吞掉)。");

            Assert.Equal(typeof(string), prop!.PropertyType);

            var getter = prop.GetGetMethod(nonPublic: true)!;

            // 属性必须直接返回常量(IL 里是 ldstr "inf"/"no"),
            // 而不能读实例字段 —— 构造期实例字段尚未赋值。
            Assert.True(
                GetterReturnsLiteral(getter, expected),
                $"{typeName}.LoopFileOption 必须直接返回字面量 \"{expected}\""
                + "(不能在构造期读实例字段,那样会被基类构造顺序吞掉)。");
        }
    }

    /// <summary>检查 getter 的 IL 中是否直接加载了期望的字符串常量(ldstr)。</summary>
    /// <remarks>
    /// <b>必须按指令边界扫描,不能逐字节扫</b>:<c>ldstr</c>(0x72) 后面紧跟 4 字节元数据 token,
    /// token 里可能出现任意字节值。原先逐字节扫描时,<c>"no"</c> 的 token(0x70013A7C)里含 0x7C,
    /// 被误判成 <c>ldflda</c> 而令测试失败 —— 实现是对的,判定方式错了。
    /// 这里按操作码宽度前进,只在真正的指令位置比较。
    /// </remarks>
    private static bool GetterReturnsLiteral(MethodInfo getter, string expected)
    {
        var il = getter.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        var module = getter.DeclaringType!.Module;
        for (var i = 0; i < il.Length;)
        {
            var op = il[i];

            // ldstr = 0x72,占 1 + 4 字节(token)
            if (op == 0x72)
            {
                if (i + 4 >= il.Length)
                {
                    return false;
                }
                var token = BitConverter.ToInt32(il, i + 1);
                try
                {
                    if (module.ResolveString(token) == expected)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // 该 token 不是字符串:跳过
                }
                i += 5;
                continue;
            }

            // ldfld = 0x7B / ldflda = 0x7C:只有位于指令边界时才算"读了实例字段"
            if (op is 0x7B or 0x7C)
            {
                return false;
            }

            // 其余指令只需正确跳过操作数,避免把操作数字节误当指令。
            // 本方法只处理表达式体属性返回常量这一种形态(必然极短),
            // 遇到无法安全判定的操作码时按 1 字节前进即可 —— 真正的字段读取会命中上面的分支。
            i += OpCodeLength(il, i);
        }

        return false;
    }

    /// <summary>返回从 <paramref name="index"/> 起的指令总长度(操作码 + 操作数)。</summary>
    private static int OpCodeLength(byte[] il, int index)
    {
        var op = il[index];

        // 双字节操作码 0xFE xx
        if (op == 0xFE)
        {
            return index + 1 < il.Length ? 4 : 1;
        }

        return op switch
        {
            // 1 字节操作数(ldarg.s / ldc.i4.s / br.s 等短格式)
            0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x1F
                or 0x2B or 0x2C or 0x2D or 0x2E or 0x2F
                or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 => 2,
            // 4 字节操作数(ldstr / call / newobj / ldc.i4 / ldfld 等,统一按 5 字节处理;
            // 这样即便 token 内出现 0x7B/0x7C 也不会被误认为指令)
            0x20 or 0x22 or 0x28 or 0x6F or 0x72 or 0x73 or 0x7B or 0x7C or 0x7E or 0x80 => 5,
            // 默认:保守按 1 字节前进(表达式体常量 getter 极短,不会漏判)
            _ => 1,
        };
    }

    /// <summary>
    /// 两个子类都继承同一基类,保证除循环选项外的其余 mpv 选项只维护一份
    /// (vo/audio/hwdec/缓冲/UA 等)。
    /// </summary>
    [Fact]
    public void 两个子类共享同一基类()
    {
        var baseType = Nested("BackgroundMpvContext");

        Assert.Equal(baseType, Nested("LoopingMpvContext").BaseType);
        Assert.Equal(baseType, Nested("OneShotMpvContext").BaseType);
    }
}
