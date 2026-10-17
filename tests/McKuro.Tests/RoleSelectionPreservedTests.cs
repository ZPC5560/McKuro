using McKuro.Core.Models.Roles;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 角色列表刷新(手动「同步」/读缓存)后<b>选中项保持</b>的回归:
/// <see cref="RolesViewModel.PickRolePreservingSelection"/> 的契约。
/// <para>
/// 背景(用户报障):角色数据页点「同步」后攻略站数据没同步过来,切走再切回才刷新。
/// 排查中发现同步还会把用户正在看的角色顶回列表首位(实测:看卡卡罗点同步 → 跳到丽贝卡),
/// 于是刚拉到的攻略/详情数据全落在另一个角色上 —— 用户视角同样是"同步的数据没过来"。
/// </para>
/// <para>
/// 该纯函数就是那条行为的判定点:去掉"按 cardRoleId 沿用旧选中"的分支(退回无条件
/// <c>ordered[0]</c>)时,前两个用例会真实失败。
/// </para>
/// </summary>
public class RoleSelectionPreservedTests
{
    private static RoleDetail Role(int cardRoleId, string name, int star = 5)
        => new()
        {
            Role = new RoleInfo
            {
                RoleId = cardRoleId,
                RoleName = name,
                StarLevel = star,
            },
        };

    /// <summary>模拟 ApplyRoles 重建后的有序列表(星级降序 + 名称序,与生产排序口径一致)。</summary>
    private static List<RoleDetail> Ordered() =>
    [
        Role(1308, "丽贝卡"),
        Role(1301, "卡卡罗", 4),
        Role(1209, "莫宁", 4),
    ];

    [Fact]
    public void Keeps_Previously_Selected_Role_Not_First()
    {
        var ordered = Ordered();

        // 用户正在看"卡卡罗"(不是列表首位),同步后应仍然是卡卡罗
        var picked = RolesViewModel.PickRolePreservingSelection(ordered, 1301);

        Assert.NotNull(picked);
        Assert.Equal(1301, picked!.Role!.RoleId);
        Assert.Equal("卡卡罗", picked.RoleName);
        // 回归点:修复前这里会是列表首位(丽贝卡)
        Assert.NotEqual(ordered[0].Role!.RoleId, picked.Role.RoleId);
    }

    [Fact]
    public void Returns_Rebuilt_Instance_Not_Stale_One()
    {
        var ordered = Ordered();
        var stale = Role(1301, "卡卡罗", 4);

        // 列表整份重建后,选中的必须是新集合里的实例(否则详情/攻略数据落在被丢弃的旧实例上)
        var picked = RolesViewModel.PickRolePreservingSelection(ordered, 1301);

        Assert.Same(ordered[1], picked);
        Assert.NotSame(stale, picked);
    }

    [Fact]
    public void Falls_Back_To_First_When_Previous_Role_Gone()
    {
        var ordered = Ordered();

        // 选中角色已被移除(换号/阵容变动)→ 回退列表首位
        var picked = RolesViewModel.PickRolePreservingSelection(ordered, 9999);

        Assert.Same(ordered[0], picked);
    }

    [Fact]
    public void Falls_Back_To_First_When_Nothing_Selected_Before()
    {
        var ordered = Ordered();

        // 首次加载(此前无选中)→ 列表首位
        var picked = RolesViewModel.PickRolePreservingSelection(ordered, 0);

        Assert.Same(ordered[0], picked);
    }

    [Fact]
    public void Returns_Null_For_Empty_List()
    {
        Assert.Null(RolesViewModel.PickRolePreservingSelection([], 1301));
    }
}
