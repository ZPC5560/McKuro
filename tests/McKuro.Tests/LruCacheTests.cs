using McKuro.Controls;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 泛型 LRU 核心逻辑测试(与 Avalonia 无关,无需平台初始化)。
/// </summary>
public class LruCacheTests
{
    private static LruCache<string, string> Cache(int entries = 100, long weight = 0)
        => new(entries, weight);

    [Fact]
    public void Set_And_Get_RoundTrip()
    {
        var cache = Cache(4, 1024);
        cache.Set("a", "v1", 10);

        Assert.True(cache.TryGet("a", out var got));
        Assert.Equal("v1", got);
        Assert.Equal(1, cache.Count);
        Assert.Equal(10, cache.CurrentWeight);
    }

    [Fact]
    public void Evicts_LeastRecentlyUsed_When_EntryCap_Hit()
    {
        var cache = Cache(3);
        cache.Set("a", "1", 1);
        cache.Set("b", "2", 1);
        cache.Set("c", "3", 1);
        cache.Set("d", "4", 1);

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void Touching_Item_Raises_Recency()
    {
        var cache = Cache(3);
        cache.Set("a", "1", 1);
        cache.Set("b", "2", 1);
        cache.Set("c", "3", 1);

        // Touch a so it becomes most-recently used
        Assert.True(cache.TryGet("a", out _));

        cache.Set("d", "4", 1);
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));  // b 是 LRU,被淘汰
    }

    [Fact]
    public void Evicts_When_WeightCap_Hit()
    {
        // 权重上限 12,每项 4 -> 第 4 项加入时淘汰最旧的
        var cache = Cache(100, 12);
        cache.Set("a", "1", 4);
        cache.Set("b", "2", 4);
        cache.Set("c", "3", 4);
        Assert.Equal(12, cache.CurrentWeight);

        cache.Set("d", "4", 4);
        Assert.True(cache.CurrentWeight <= 12);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("d", out _));
    }

    [Fact]
    public void Update_Existing_Key_Adjusts_Weight()
    {
        var cache = Cache(100, 100);
        cache.Set("a", "v1", 10);
        cache.Set("a", "v2", 30);
        Assert.Equal(30, cache.CurrentWeight);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("a", out var got));
        Assert.Equal("v2", got);
    }

    [Fact]
    public void Clear_Wipes_Cache()
    {
        var cache = Cache(4, 1024);
        cache.Set("a", "1", 8);
        cache.Set("b", "2", 8);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CurrentWeight);
    }

    [Fact]
    public void No_WeightLimit_Only_Evicts_By_Entries()
    {
        // MaxWeight = 0 → 不按权重淘汰,只按条数
        var cache = Cache(2, 0);
        cache.Set("a", "1", 999);
        cache.Set("b", "2", 999);
        cache.Set("c", "3", 999);
        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(999 * 2, cache.CurrentWeight);
    }
}
