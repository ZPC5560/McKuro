namespace donet.Core.Models.Gacha;

/// <summary>角色/武器图标目录(ResourceId → mc.appfeng.com 图标文件名)。</summary>
/// <remarks>数据来源:mc.appfeng.com 图鉴(与 Haiyu 一致);缺少的 ResourceId 返回空串。</remarks>
public static class IconCatalog
{
    private static readonly IReadOnlyDictionary<int, string> Roles = new Dictionary<int, string>
    {
        [1102] = "T_IconRoleHead256_7_UI", // 散华
        [1103] = "T_IconRoleHead256_6_UI", // 白芷
        [1104] = "T_IconRoleHead256_14_UI", // 凌阳
        [1105] = "T_IconRoleHead256_27_UI", // 折枝
        [1106] = "T_IconRoleHead256_31_UI", // 釉瑚
        [1107] = "T_IconRoleHead256_32_UI", // 珂莱塔
        [1108] = "T_IconRoleHead256_67_UI", // 绯雪
        [1109] = "T_IconRoleHead256_66_UI", // 洛瑟菈
        [1110] = "T_IconRoleHead256_71_UI", // 穗穗
        [1202] = "T_IconRoleHead256_2_UI", // 炽霞
        [1203] = "T_IconRoleHead256_8_UI", // 安可
        [1204] = "T_IconRoleHead256_13_UI", // 莫特斐
        [1205] = "T_IconRoleHead256_26_UI", // 长离
        [1206] = "T_IconRoleHead256_44_UI", // 布兰特
        [1207] = "T_IconRoleHead256_46_UI", // 露帕
        [1208] = "T_IconRoleHead256_55_UI", // 嘉贝莉娜
        [1209] = "T_IconRoleHead256_61_UI", // 莫宁
        [1210] = "T_IconRoleHead256_53_UI", // 爱弥斯
        [1211] = "T_IconRoleHead256_64_UI", // 达妮娅
        [1301] = "T_IconRoleHead256_18_UI", // 卡卡罗
        [1302] = "T_IconRoleHead256_17_UI", // 吟霖
        [1303] = "T_IconRoleHead256_15_UI", // 渊武
        [1304] = "T_IconRoleHead256_24_UI", // 今汐
        [1305] = "T_IconRoleHead256_25_UI", // 相里要
        [1306] = "T_IconRoleHead256_51_UI", // 奥古斯塔
        [1307] = "T_IconRoleHead256_58_UI", // 卜灵
        [1308] = "T_IconRoleHead256_69_UI", // 丽贝卡
        [1309] = "T_IconRoleHead256_4_UI", // 漂泊者·导电
        [1310] = "T_IconRoleHead256_5_UI", // 漂泊者·导电
        [1402] = "T_IconRoleHead256_1_UI", // 秧秧
        [1403] = "T_IconRoleHead256_12_UI", // 秋水
        [1404] = "T_IconRoleHead256_11_UI", // 忌炎
        [1405] = "T_IconRoleHead256_23_UI", // 鉴心
        [1406] = "T_IconRoleHead256_4_UI", // 漂泊者·气动
        [1407] = "T_IconRoleHead256_37_UI", // 夏空
        [1408] = "T_IconRoleHead256_5_UI", // 漂泊者·气动
        [1409] = "T_IconRoleHead256_40_UI", // 卡提希娅
        [1410] = "T_IconRoleHead256_48_UI", // 尤诺
        [1411] = "T_IconRoleHead256_56_UI", // 仇远
        [1412] = "T_IconRoleHead256_65_UI", // 西格莉卡
        [1501] = "T_IconRoleHead256_4_UI", // 漂泊者·衍射
        [1502] = "T_IconRoleHead256_5_UI", // 漂泊者·衍射
        [1503] = "T_IconRoleHead256_3_UI", // 维里奈
        [1504] = "T_IconRoleHead256_30_UI", // 灯灯
        [1505] = "T_IconRoleHead256_28_UI", // 守岸人
        [1506] = "T_IconRoleHead256_45_UI", // 菲比
        [1507] = "T_IconRoleHead256_38_UI", // 赞妮
        [1508] = "T_IconRoleHead256_57_UI", // 千咲
        [1509] = "T_IconRoleHead256_60_UI", // 琳奈
        [1510] = "T_IconRoleHead256_54_UI", // 陆·赫斯
        [1511] = "T_IconRoleHead256_68_UI", // 露西
        [1601] = "T_IconRoleHead256_9_UI", // 桃祈
        [1602] = "T_IconRoleHead256_10_UI", // 丹瑾
        [1603] = "T_IconRoleHead256_29_UI", // 椿
        [1604] = "T_IconRoleHead256_5_UI", // 漂泊者·湮灭
        [1605] = "T_IconRoleHead256_4_UI", // 漂泊者·湮灭
        [1606] = "T_IconRoleHead256_33_UI", // 洛可可
        [1607] = "T_IconRoleHead256_34_UI", // 坎特蕾拉
        [1608] = "T_IconRoleHead256_41_UI", // 弗洛洛
        [1610] = "T_IconRoleHead256_70_UI", // 秧秧·玄翎
    };

    private static readonly IReadOnlyDictionary<int, string> Weapons = new Dictionary<int, string>
    {
        [21010011] = "T_IconWeapon21010011_UI", // 教学长刃
        [21010012] = "T_IconWeapon21010012_UI", // 原初长刃·朴石
        [21010013] = "T_IconWeapon21010013_UI", // 暗夜长刃·玄明
        [21010015] = "T_IconWeapon21010015_UI", // 浩境粼光
        [21010016] = "T_IconWeapon21010016_UI", // 苍鳞千嶂
        [21010023] = "T_IconWeapon21010023_UI", // 源能长刃·测壹
        [21010024] = "T_IconWeapon21010024_UI", // 异响空灵
        [21010026] = "T_IconWeapon21010026_UI", // 时和岁稔
        [21010034] = "T_IconWeapon21010034_UI", // 重破刃-41型
        [21010036] = "T_IconWeapon21010036_UI", // 焰痕
        [21010043] = "T_IconWeapon21010043_UI", // 远行者长刃·辟路
        [21010044] = "T_IconWeapon21010044_UI", // 永夜长明
        [21010045] = "T_IconWeapon_21010045_UI", // 源能机锋
        [21010046] = "T_IconWeapon21010046_UI", // 驭冕铸雷之权
        [21010053] = "T_IconWeapon21010053_UI", // 戍关长刃·定军
        [21010056] = "T_IconWeapon21010056_UI", // 昙切
        [21010063] = "T_IconWeapon21010063_UI", // 钧天正音
        [21010064] = "T_IconWeapon21010064_UI", // 东落
        [21010066] = "T_IconWeapon21010066_UI", // 宙算仪轨
        [21010074] = "T_IconWeapon21010074_UI", // 纹秋
        [21010084] = "T_IconWeapon21010084_UI", // 凋亡频移
        [21010094] = "T_IconWeapon21010094_UI", // 容赦的沉思录
        [21010104] = "T_IconWeapon21010104_UI", // 金穹
        [21020011] = "T_IconWeapon21020011_UI", // 教学迅刀
        [21020012] = "T_IconWeapon21020012_UI", // 原初迅刀·鸣雨
        [21020013] = "T_IconWeapon21020013_UI", // 暗夜迅刀·黑闪
        [21020015] = "T_IconWeapon21020015_UI", // 千古洑流
        [21020016] = "T_IconWeapon21020016_UI", // 赫奕流明
        [21020017] = "T_IconWeapon21020019_UI", // 心之锚
        [21020023] = "T_IconWeapon21020023_UI", // 源能迅刀·测贰
        [21020024] = "T_IconWeapon21020024_UI", // 行进序曲
        [21020026] = "T_IconWeapon21020017_UI", // 裁春
        [21020034] = "T_IconWeapon21020034_UI", // 瞬斩刀-18型
        [21020036] = "T_IconWeapon21020025_UI", // 不灭航路
        [21020043] = "T_IconWeapon21020043_UI", // 远行者迅刀·旅迹
        [21020044] = "T_IconWeapon21020044_UI", // 不归孤军
        [21020045] = "T_IconWeapon_21020045_UI", // 镭射切变
        [21020046] = "T_IconWeapon21020026_UI", // 血誓盟约
        [21020053] = "T_IconWeapon21020053_UI", // 戍关迅刀·镇海
        [21020056] = "T_IconWeapon21020056_UI", // 不屈命定之冠
        [21020064] = "T_IconWeapon21020064_UI", // 西升
        [21020066] = "T_IconWeapon21020066_UI", // 裁竹
        [21020074] = "T_IconWeapon21020074_UI", // 飞景
        [21020076] = "T_IconWeapon21020076_UI", // 永远的启明星
        [21020084] = "T_IconWeapon21020084_UI", // 永续坍缩
        [21020086] = "T_IconWeapon21020086_UI", // 灼霜
        [21020094] = "T_IconWeapon21020094_UI", // 风流的寓言诗
        [21020096] = "T_IconWeapon21020096_UI", // 天之苍苍
        [21020104] = "T_IconWeapon21020104_UI", // 翼锋
        [21030011] = "T_IconWeapon21030011_UI", // 教学佩枪
        [21030012] = "T_IconWeapon21030012_UI", // 原初佩枪·穿林
        [21030013] = "T_IconWeapon21030013_UI", // 暗夜佩枪·暗星
        [21030015] = "T_IconWeapon21030015_UI", // 停驻之烟
        [21030016] = "T_IconWeapon21030017_UI", // 死与舞
        [21030023] = "T_IconWeapon21030023_UI", // 源能佩枪·测叁
        [21030024] = "T_IconWeapon21030024_UI", // 华彩乐段
        [21030026] = "T_IconWeapon21030026_UI", // 林间的咏叹调
        [21030034] = "T_IconWeapon21030034_UI", // 穿击枪-26型
        [21030036] = "T_IconWeapon21030036_UI", // 光影双生
        [21030043] = "T_IconWeapon21030043_UI", // 远行者佩枪·洞察
        [21030044] = "T_IconWeapon21030044_UI", // 无眠烈火
        [21030045] = "T_IconWeapon_21030045_UI", // 相位涟漪
        [21030046] = "T_IconWeapon21030046_UI", // 溢彩荧辉
        [21030053] = "T_IconWeapon21030053_UI", // 戍关佩枪·平云
        [21030056] = "T_IconWeapon21030056_UI", // 蜃影
        [21030064] = "T_IconWeapon21030064_UI", // 飞逝
        [21030066] = "T_IconWeapon21030066_UI", // 碎骨
        [21030074] = "T_IconWeapon21030074_UI", // 奔雷
        [21030084] = "T_IconWeapon21030084_UI", // 悖论喷流
        [21030094] = "T_IconWeapon21030094_UI", // 叙别的罗曼史
        [21030104] = "T_IconWeapon21030104_UI", // 阳焰
        [21040011] = "T_IconWeapon21040011_UI", // 教学臂铠
        [21040012] = "T_IconWeapon21040012_UI", // 原初臂铠·磐岩
        [21040013] = "T_IconWeapon21040013_UI", // 暗夜臂铠·夜芒
        [21040015] = "T_IconWeapon21040015_UI", // 擎渊怒涛
        [21040016] = "T_IconWeapon21040016_UI", // 诸方玄枢
        [21040023] = "T_IconWeapon21040023_UI", // 源能臂铠·测肆
        [21040024] = "T_IconWeapon21040024_UI", // 呼啸重音
        [21040026] = "T_IconWeapon21040018_UI", // 悲喜剧
        [21040034] = "T_IconWeapon21040034_UI", // 钢影拳-21丁型
        [21040036] = "T_IconWeapon21040019_UI", // 焰光裁定
        [21040043] = "T_IconWeapon21040043_UI", // 远行者臂铠·破障
        [21040044] = "T_IconWeapon21040044_UI", // 袍泽之固
        [21040045] = "T_IconWeapon_21040045_UI", // 脉冲协臂
        [21040046] = "T_IconWeapon21040046_UI", // 万物持存的注释
        [21040053] = "T_IconWeapon21040053_UI", // 戍关臂铠·拔山
        [21040056] = "T_IconWeapon_21040056_UI", // 白昼之脊
        [21040064] = "T_IconWeapon21040064_UI", // 骇行
        [21040066] = "T_IconWeapon21040066_UI", // 昭日译注
        [21040074] = "T_IconWeapon21040074_UI", // 金掌
        [21040084] = "T_IconWeapon21040084_UI", // 尘云旋臂
        [21040094] = "T_IconWeapon21040094_UI", // 酩酊的英雄志
        [21040104] = "T_IconWeapon21040104_UI", // 凌空
        [21050011] = "T_IconWeapon21050011_UI", // 教学音感仪
        [21050012] = "T_IconWeapon21050012_UI", // 原初音感仪·听浪
        [21050013] = "T_IconWeapon21050013_UI", // 暗夜矩阵·暝光
        [21050015] = "T_IconWeapon21050015_UI", // 漪澜浮录
        [21050016] = "T_IconWeapon21050016_UI", // 掣傀之手
        [21050017] = "T_IconWeapon21050017_UI", // 渊海回声
        [21050023] = "T_IconWeapon21050023_UI", // 源能音感仪·测五
        [21050024] = "T_IconWeapon21050024_UI", // 奇幻变奏
        [21050026] = "T_IconWeapon21050026_UI", // 琼枝冰绡
        [21050027] = "T_IconWeapon21050036_UI", // 大海的馈赠
        [21050034] = "T_IconWeapon21050034_UI", // 鸣动仪-25型
        [21050036] = "T_IconWeapon21050027_UI", // 星序协响
        [21050043] = "T_IconWeapon21050043_UI", // 远行者矩阵·探幽
        [21050044] = "T_IconWeapon21050044_UI", // 今州守望
        [21050045] = "T_IconWeapon_21050045_UI", // 玻色星仪
        [21050046] = "T_IconWeapon21050029_UI", // 和光回唱
        [21050053] = "T_IconWeapon21050053_UI", // 戍关音感仪·留光
        [21050056] = "T_IconWeapon21050030_UI", // 海的呢喃
        [21050064] = "T_IconWeapon21050064_UI", // 异度
        [21050066] = "T_IconWeapon21050066_UI", // 幽冥的忘忧章
        [21050074] = "T_IconWeapon21050074_UI", // 清音
        [21050076] = "T_IconWeapon21050076_UI", // 赝作的矮星
        [21050084] = "T_IconWeapon21050084_UI", // 核熔星盘
        [21050086] = "T_IconWeapon21050086_UI", // 存帧
        [21050094] = "T_IconWeapon21050094_UI", // 虚饰的华尔兹
        [21050096] = "T_IconWeapon21050096_UI", // 栖霞饮露
        [21050104] = "T_IconWeapon21050104_UI", // 曜光
    };

    /// <summary>角色头像 URL(未收录返回空串)。</summary>
    public static string GetRoleIconUrl(int resourceId) =>
        Roles.TryGetValue(resourceId, out var icon) ? $"https://mc.appfeng.com/ui/avatar/{icon}.png" : "";

    /// <summary>武器图标 URL(未收录返回空串)。</summary>
    public static string GetWeaponIconUrl(int resourceId) =>
        Weapons.TryGetValue(resourceId, out var icon) ? $"https://mc.appfeng.com/ui/weapon/{icon}.png" : "";

    /// <summary>按抽卡记录类型返回图标 URL(角色→avatar,武器→weapon)。</summary>
    public static string GetIconUrl(GachaRecord record) =>
        record.IsRole ? GetRoleIconUrl(record.ResourceId) : GetWeaponIconUrl(record.ResourceId);
}
