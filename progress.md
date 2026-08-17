# Progress Log

## Session: 2026-08-17

### Phase 1: 现状盘点与故障定位
- **Status:** complete
- **Started:** 2026-08-17
- Actions taken:
  - 明确动态壁纸、主题联动、玻璃效果和视频修复四项需求。
  - 创建持久化规划文件。
  - 搜索主题、背景、视频、设置与颜色提取相关代码。
  - 确认现有界面广泛依赖 Semi 动态资源，具备全局主题覆盖基础。
  - 定位官方视频主故障：视频控件未在 `LauncherView.axaml` 中使用，视频 URL 与开关没有界面绑定。
  - 检查应用壳层和首页，确认壁纸应提升到 `MainWindow` 背景层，首页与普通页面使用透明/玻璃语义表面覆盖。
  - 检查设置持久化与依赖注册，确认新增壁纸/主题服务可以沿用现有 JSON 设置和集中 DI 架构。
  - 检查取色算法与项目依赖，确认可扩展现有 `ColorThiefHelper`，并发现 Windows 原生 LibVLC 包当前未随项目引用。
  - 核对 Avalonia 与 VideoLAN 官方资料，确定玻璃效果必须有性能/平台降级，视频需要随包提供平台原生 LibVLC。
  - 检查 Git 历史，锁定视频功能引入与首页重构两个关键提交，准备判定回归来源。
  - 确认提交 `387362a` 删除了视频控件绑定；修复方案将恢复到启动页全屏背景层，并保留静态首帧回退。
  - 完成页面背景策略分类，并确定设置页新增“个性化”分组承载壁纸、配色和玻璃选项。
  - 实测官方 MP4 地址有效且支持范围请求；确认本机缺少 VLC，进一步证明运行时打包是视频修复必要项。
  - 完成故障定位和现有架构盘点。
- Files created/modified:
  - `task_plan.md`（创建）
  - `findings.md`（创建）
  - `progress.md`（创建）

### Phase 2: 视觉规范与交互方案
- **Status:** complete
- Actions taken:
  - 定义主页、启动页和普通功能页三类背景策略。
  - 定义普通玻璃、Dense 玻璃和液态玻璃的使用边界。
  - 定义壁纸选择、动态配色、恢复默认和性能降级交互。
- Files created/modified:
  - `task_plan.md`（更新）
  - `findings.md`（更新）
  - `progress.md`（更新）

### Phase 3: 技术架构设计
- **Status:** complete
- Actions taken:
  - 设计 WallpaperService、ThemePaletteService、全局背景宿主和玻璃语义资源。
  - 设计官方视频的全屏接入、原生运行时打包、状态事件、日志与资源释放。
  - 定义分四批实施顺序和验收标准。
- Files created/modified:
  - `task_plan.md`（更新）
  - `findings.md`（更新）
  - `progress.md`（更新）

### Phase 4: 分阶段实施
- **Status:** in_progress
- Actions taken:
  - 用户确认方案后开始实施。
  - 加载并遵循 `frontend-skill`，以用户旅程、层次、主次操作、响应式约束和可降级状态指导 Avalonia 界面改动。
- 首轮范围确定为：`AppSettings` 个性化字段、`WallpaperService`、`ThemePaletteService`、`PlatformCapabilities`、主窗口背景层、设置页壁纸入口、启动器视频背景恢复。
- 交付方式已确定：完成后创建独立功能分支并推送，保留 `main` 不变。
- 已完成：新增个性化设置字段、跨平台能力服务、壁纸托管服务、壁纸取色与语义资源服务，并完成 DI 注册。
- 已完成：主窗口新增全局壁纸、普通页面单实例模糊背景、动态导航/内容玻璃资源；主页默认深色渐变已改为壁纸上的轻量遮罩。
- 已完成：设置页新增平台能力提示、壁纸预览/选择/恢复默认、动态配色开关和玻璃质量档位。
- 已完成：启动器恢复全屏 `VideoBackgroundControl`，按官方 `backgroundFileType` 区分图片/视频，加入首帧回退、错误状态、静音、循环播放线程切换、Media/Player/View 释放和 Windows LibVLC 随包引用。
- 已完成：非 Windows 平台在启动器页明确显示能力提示并隐藏 Windows 游戏安装/修复/启动动作；输出类型、Windows manifest 和媒体包按 RID 条件处理。
- 待验证：NuGet 媒体大包下载失败，尚未完成本机真实视频播放和完整构建；代码与 XAML 静态检查继续进行。
- 静态检查：`git diff --check` 无空白错误；当前环境 SDK 为 10.0.302，而仓库 `global.json` 要求 10.0.400，正式构建前需用可用 SDK 或调整本地 SDK 解析方式验证。
- 构建验证：`McKuro.slnx` 首次构建因无 `obj/project.assets.json` 需要还原；NuGet 下载 120 MB 的 `VideoLAN.LibVLC.Windows` 时连续出现 `ResponseEnded`，已停止重试，准备暂时跳过该大包做代码编译验证，随后恢复引用。
- Files created/modified:
  - 即将开始修改业务实现。

### Plan Revision: Multi-platform requirement
- **Status:** complete
- Actions taken:
  - 将 Windows、macOS、桌面 Linux 设为硬性平台目标。
  - 核对 Avalonia 各平台透明能力与 LibVLCSharp 原生依赖差异。
  - 确定玻璃视觉以应用内渲染为基线，系统 Acrylic/Mica 仅作 Windows 增强。
  - 确定视频需要平台原生库解析层和各平台独立打包策略。
  - 搜索平台相关代码，确认主 UI 可跨平台，但游戏启动、PC 本地缓存和应用更新需要能力分层与降级。
  - 检查发布基础设施，确认当前仅有 Windows Inno Setup 和少量手动 publish 命令，没有实际 GitHub Actions 工作流。
  - 核对游戏官方平台：macOS 有 Apple Silicon 原生版本；Linux 未验证到官方原生版本且 Steam 页面标明内核级反作弊，因此定义不同功能等级。
  - 更新实施计划，加入 Windows/macOS/Linux 支持矩阵、PlatformCapabilities、IGamePlatformAdapter、平台媒体运行时和跨平台发布流水线。
- Files created/modified:
  - `task_plan.md`（已更新多平台架构）
  - `findings.md`（更新）
  - `progress.md`（更新）

## Test Results
| Test | Input | Expected | Actual | Status |
|------|-------|----------|--------|--------|
| Planning files created | Repository root | Three planning files exist | Created | Pass |
| Official background API | Current official endpoint | Video metadata and reachable MP4 | Type 2, HTTP 200, video/mp4, range supported | Pass |
| Video regression trace | Git history | Identify removal commit | Binding removed in `387362a` | Pass |

## Error Log
| Timestamp | Error | Attempt | Resolution |
|-----------|-------|---------|------------|
| 2026-08-17 | `git show` 参数顺序错误导致无输出 | 1 | 使用 `git show 387362a -- <paths>` |
| 2026-08-17 | 查询 `.github` 目录失败 | 1 | 确认仓库未提供该目录，并纳入跨平台 CI 新增项 |

## 5-Question Reboot Check
| Question | Answer |
|----------|--------|
| Where am I? | Phase 4：正在分阶段实施 |
| Where am I going? | 壁纸/主题基础、玻璃迁移、视频修复和验收 |
| What's the goal? | 制定 McKuro 动态壁纸、自适应玻璃主题和视频修复方案 |
| What have I learned? | 见 `findings.md` |
| What have I done? | 已完成现状盘点、视觉规范、技术架构和验收标准 |
