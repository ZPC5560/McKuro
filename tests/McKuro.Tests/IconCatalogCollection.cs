namespace McKuro.Tests;

/// <summary>
/// 串行集合:<see cref="McKuro.Core.Models.Gacha.IconCatalog"/> 的远程目录是<b>进程级静态状态</b>,
/// 而 xUnit 默认并行执行不同测试类。GachaIconCatalogTests 注入远程目录的同时,
/// LauncherInfoTests 会断言同一静态字典的兜底行为,并行跑必然互相干扰(偶发假失败)。
/// 凡读写 IconCatalog 的测试类都必须加入本集合以强制串行。
/// </summary>
[CollectionDefinition(IconCatalogCollection.Name, DisableParallelization = true)]
public sealed class IconCatalogCollection
{
    public const string Name = "IconCatalogStaticState";
}
