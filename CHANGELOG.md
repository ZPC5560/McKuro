# Changelog

## [1.3.0] - 2026-10-01

### Changed

- 将 Live2D 显示参数移到预览区右侧,并修复亮色主题下数值框与白色卡片同色而几乎不可见 ([`a70b2d6`](https://github.com/ZPC5560/McKuro/commit/a70b2d6))
- 把通知判定收敛为「总开关 × 五类分类开关」的唯一入口,避免某条链路绕过总开关 ([`a70b2d6`](https://github.com/ZPC5560/McKuro/commit/a70b2d6))
- 让抽卡图鉴图标按目录缓存,减少重复拉取 ([`b55790c`](https://github.com/ZPC5560/McKuro/commit/b55790c))

### Added

- 新增资源等级分级下载与切换:按已安装资源计算差异包,切换画质只下载目标等级缺失的部分(实测 HD/SD/UHD 等级包互不相交,common 已装则跳过) ([`faf8d43`](https://github.com/ZPC5560/McKuro/commit/faf8d43))
- 新增设置页二级导航,按分类拆分子标签 ([`a70b2d6`](https://github.com/ZPC5560/McKuro/commit/a70b2d6))
- 新增桌面快捷方式创建 (`WScript.Shell` 生成 `.lnk`) ([`770700d`](https://github.com/ZPC5560/McKuro/commit/770700d))
- 新增游戏菜单中的「检查游戏更新」入口 ([`770700d`](https://github.com/ZPC5560/McKuro/commit/770700d))

### Removed

- 移除主页副标题、快捷操作按钮与「已更新」状态小字 ([`770700d`](https://github.com/ZPC5560/McKuro/commit/770700d))

### Fixed

- 修复游戏在缺少资源等级启动参数时无法启动:现按已安装资源推导并默认注入 `-krqlv`,不再需要手动配置启动参数 ([`faf8d43`](https://github.com/ZPC5560/McKuro/commit/faf8d43))
- 修复安装/修复阶段误显示「暂停下载」按钮:该阶段暂停无效,且残留暂停态会让后续真实下载静默阻塞;改为仅下载阶段可暂停 ([`5767ff5`](https://github.com/ZPC5560/McKuro/commit/5767ff5))
- 修复海墟赛季翻新后仍展示上期成绩:接口在新赛季仍返回上一期数据且 9/10/11 关不清零,页面会把上赛季满档成绩当作本期;现只保留跨赛季延续的第 7、8 关并按实际关卡重算总分 ([`d4efc2e`](https://github.com/ZPC5560/McKuro/commit/d4efc2e))
- 修复终焉矩阵已结束的一期仍挂在「挑战模式」下,同时出现在本期与往期历史两处 ([`d4efc2e`](https://github.com/ZPC5560/McKuro/commit/d4efc2e))
- 修复深塔卡片换行后上下贴死 (`WrapPanel.LineSpacing` 默认 0) ([`d4efc2e`](https://github.com/ZPC5560/McKuro/commit/d4efc2e))
- 修复角色列表同步后未落盘,重开页面退回旧角色数量:现同步收尾整体合并写回,且角色集合以最新列表为准、旧缓存仅用于补详情 ([`16e733c`](https://github.com/ZPC5560/McKuro/commit/16e733c))
- 修复武器活动池显示无意义的「不歪率」:该池必中 UP、歪率恒为 0,现仅 50/50 池展示 ([`b55790c`](https://github.com/ZPC5560/McKuro/commit/b55790c))

[1.3.0]: https://github.com/ZPC5560/McKuro/releases/tag/v1.3.0
