# Task Plan: McKuro 动态壁纸与自适应玻璃主题

## Goal
为 McKuro 制定并实施一套可在 Windows、macOS、桌面 Linux 使用的 UI 改造：用户可更换主页壁纸，应用主题从壁纸自动生成；非固定展示内容使用毛玻璃或液态玻璃视觉；官方视频背景能在各平台播放或可靠降级；平台专属游戏功能通过能力适配器明确启用或禁用。

## Current Phase
Phase 4（实施中：先完成壁纸、动态配色、玻璃视觉基座与视频背景回归）

交付约束：实现完成后从当前 `main` 新建独立功能分支，提交并推送到远程功能分支；不直接提交、不强推、不覆盖 `main`。

## Phases

### Phase 1: 现状盘点与故障定位
- [x] 识别主题、首页背景、设置持久化和视频播放相关代码
- [x] 梳理当前数据流与平台限制
- [x] 定位视频无法播放的故障链路与回归提交
- **Status:** complete

### Phase 2: 视觉规范与交互方案
- [x] 定义壁纸选择、即时预览、恢复默认和失败提示流程
- [x] 定义动态色板、对比度和浅深模式规则
- [x] 定义普通毛玻璃与液态玻璃的使用边界
- **Status:** complete

### Phase 3: 技术架构设计
- [x] 设计 WallpaperService 与设置持久化
- [x] 设计 ThemePaletteService 与全局动态资源
- [x] 设计 GlassSurface 样式/控件及性能降级
- [x] 设计 VideoBackgroundControl 修复与诊断机制
- **Status:** complete

### Phase 4: 分阶段实施
- [ ] 当前实现批次：先完成壁纸、动态配色、主窗口背景层、设置入口、玻璃资源和视频背景回归
- [x] 批次 A：完成 PlatformCapabilities、平台适配器、设置模型、WallpaperService、ThemePaletteService 与单元测试
- [x] 批次 B：改造 MainWindow 背景宿主、主页和“个性化”设置区
- [x] 批次 C：新增 GlassSurface 语义资源并通过 Semi 基础表面桥接迁移导航、普通数据页和弹层
- [ ] 批次 D：恢复启动页全屏视频绑定，实现 Windows/macOS/Linux 原生媒体运行时解析、播放器状态与回退
- [ ] 批次 E：补齐跨平台 CI、安装包、Release 资产和平台自更新策略
- **Status:** in_progress

### Phase 5: 测试与视觉验收
- [ ] 覆盖设置持久化、色板和视频状态单元测试
- [ ] 验证不同明暗壁纸下的可读性
- [ ] 验证断网、视频失败、非视频背景和低性能降级
- [ ] 验证 Windows 发布包在未安装系统 VLC 的干净环境能播放视频
- [ ] 验证 macOS `.app` 内原生媒体库、签名和视频播放；不可用时静态图降级
- [ ] 验证 Linux 有/无系统 libvlc 两种路径；缺失时不崩溃并显示诊断
- [ ] 连续导航启动页 20 次，确认播放器停止/重建正常且无明显资源增长
- [ ] 在 Windows、macOS、Ubuntu runner 分别执行 restore/build/test/publish
- **Status:** pending

## Supported Platform Matrix

| Platform | Release RID | UI/主题/玻璃 | 官方视频 | 游戏能力 |
|----------|-------------|--------------|----------|----------|
| Windows 10/11 x64 | `win-x64` | 完整 | 随包携带 LibVLC，完整 | 现有安装、更新、修复、日志、启动完整保留 |
| macOS 12+ Apple Silicon | `osx-arm64` | 完整 | `.app` 内捆绑/定位 LibVLC；失败回退首帧 | 识别并打开官方 App Store `.app`；游戏更新交给 App Store |
| macOS Intel | `osx-x64` | UI/数据功能可发布 | 验证 Mac 原生库后启用 | 官方游戏要求 Apple Silicon，因此不提供游戏管理承诺 |
| Desktop Linux x64 | `linux-x64` | 完整，按合成器降级 | 优先系统 libvlc；包声明依赖/AppImage 可内置；失败回退 | 数据功能完整；可配置 Steam/兼容层启动，不承诺原生游戏与反作弊兼容 |
| Linux arm64 | `linux-arm64` | 后续扩展 | 系统 libvlc | 非首发验收目标 |

## Target Architecture

### 1. WallpaperService
- 通过设置页图片选择器接受 PNG/JPEG/WebP。
- 校验文件可读、尺寸合理后复制到 `AppServices.AppDataDir/wallpapers/current.<ext>`；由 .NET 映射为各平台应用数据目录，不依赖用户原始文件继续存在。
- 保存 `WallpaperPath`、`WallpaperStretch`、`DynamicPaletteEnabled`、`GlassQuality` 和色板缓存键。
- 提供更换、恢复默认、加载失败回退和 `WallpaperChanged` 通知。

### 2. ThemePaletteService
- 在后台把壁纸降采样后提取主色、强调色、平均亮度和饱和度。
- 根据当前 Light/Dark/Default 模式生成受约束色板；壁纸决定色相，主题模式决定表面明暗。
- 保证普通正文前景对背景至少 4.5:1，对不可控颜色进行增亮、压暗或降饱和修正。
- 一次性更新 `McKuroAccent`、`McKuroAccentHover`、`McKuroBackdropTint`、`McKuroGlassFill`、`McKuroGlassStroke`、`McKuroTextOnWallpaper` 等动态资源，并桥接少量 Semi 主色。
- 按壁纸内容哈希缓存色板，启动时无需重复分析。

### 3. App Background Host
- `MainWindow` 最底层显示全局壁纸与氛围遮罩。
- 主页显示清晰壁纸；普通功能页启用一层全局、单实例的轻模糊壁纸和更强遮罩；不在每张卡片上运行 BlurEffect。
- 启动页使用自身的官方视频/首帧背景完全覆盖全局壁纸。
- 导航栏改为动态玻璃表面，选中态只使用一个强调色。
- 系统窗口 Acrylic/Mica 不属于跨平台基线；Windows 可选增强，macOS/Linux 默认使用应用内部壁纸材质。

### 4. Glass System
- 提供 `GlassSurface`, `GlassSurface.Dense`, `LiquidGlassAction`, `GlassNavigation` 四类语义样式。
- 普通毛玻璃：半透明主题色填充 + 1px 高光描边 + 轻阴影，服务于表单、数据区和设置分组。
- 液态玻璃：仅用于导航选中态、主按钮、浮动工具条和短暂弹层；通过渐变、高光与状态动画模拟材质，不实现高成本实时折射。
- 高密度表格、图表和长文本区域使用 `Dense` 高不透明度版本，避免“漂亮但看不清”。
- `GlassQuality=Auto/High/Low/Off`；Low/Off 禁用背景模糊并提高表面不透明度。
- `Auto` 根据平台和渲染状态选档：Windows 可启用窗口背板增强；macOS 使用应用内材质；Linux X11/Wayland 均不依赖 compositor blur。

### 5. Official Video Pipeline
- 在 `LauncherView.axaml` 全屏背景层绑定 `VideoBackgroundControl`，静态首帧始终先显示。
- 仅当 `BackgroundFileType == 2`、开关启用且 URL 合法时尝试视频；图片类型直接走静态背景。
- 抽象 `INativeMediaRuntime`，负责平台检测、原生库定位、初始化结果和诊断；UI 控件只消费统一状态。
- Windows 发布加入匹配版本的 `VideoLAN.LibVLC.Windows`。
- macOS 将兼容的 LibVLC 3.x 放入 `.app` 合适目录，使用 `Core.Initialize(path)` 定位，并把 dylib/plugin 纳入签名与公证流程；实施前验证较旧 Mac NuGet 是否可用，不盲目锁定旧包。
- Linux 优先加载系统 libvlc；`.deb` 声明媒体依赖，AppImage 路线通过启动脚本设置库搜索路径。Linux 不向 `Core.Initialize(path)` 传自定义路径。
- 各平台应用启动阶段记录 LibVLC/LibVLCSharp 版本、搜索位置与失败原因。
- 播放器默认静音、循环；订阅 Opening/Buffering/Playing/EncounteredError/EndReached，在 Playing 后淡入视频，失败立即恢复静态图。
- 显式持有并释放 `Media`、`MediaPlayer` 和事件处理器；离开页面停止，返回页面可重新播放。
- 日志区分 API、下载、原生库、解码和生命周期错误；设置页显示“视频可用/已回退”的诊断状态。

### 6. Platform Capability Layer
- 新增 `PlatformCapabilities`：`CanManageGameFiles`、`CanLaunchNativeGame`、`CanReadLocalLauncherCache`、`CanAutoInstallUpdate`、`CanUseSystemBackdrop`、`CanPlayVideo`。
- 抽象 `IGamePlatformAdapter`：Windows 复用现有路径/更新器；macOS 识别 Apple Silicon App Store `.app` 并调用系统打开；Linux 接收用户明确配置的 Steam URI 或兼容层命令。
- 不支持的动作在 UI 中禁用并显示原因，而不是执行后抛出 `.exe`/路径错误。
- `LocalGameDailyDataService` 在不支持 PC 启动器缓存的平台自动跳过，回退库街区网络数据。
- 所有路径使用 `Path.Combine`、`Path.DirectorySeparatorChar` 和 `AppServices.AppDataDir`；所有外部打开动作经过平台服务。

### 7. Cross-platform Packaging & Updates
- 新增 GitHub Actions 三系统矩阵；Native AOT 在对应 OS runner 本地发布，避免跨平台 AOT。
- Windows：`win-x64` + Inno Setup `.exe`。
- macOS：`osx-arm64` 为主，生成 `.app`，随后签名、公证并输出 `.dmg` 或签名 `.zip`；`osx-x64` 仅在 UI/媒体验证通过后发布。
- Linux：`linux-x64` 输出 `.tar.gz`，随后增加 AppImage/`.deb`；包元数据声明 libvlc 和图形依赖。
- Release 资产使用清晰 RID 命名；`AppUpdateService` 按平台和架构选资产。
- 自动安装分阶段开放：Windows 保留安装器；macOS/Linux 首版下载后打开 Release/文件位置，由用户完成平台原生安装，避免绕过签名、挂载和包管理流程。

## Key Questions
1. 当前主题资源如何组织，是否支持运行时替换全局 Brush？已确认：新增应用语义资源并运行时更新。
2. 官方视频为什么不能播放？已确认：XAML 绑定被重构删除，且 Windows 原生 LibVLC 未随包提供。
3. 哪些页面适合透明玻璃？已确认：首页清晰壁纸、启动页官方媒体、其余页面按内容密度使用普通或 Dense 玻璃。
4. 壁纸如何持久化？已决定：复制到应用数据目录并保存受管路径。

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| 先完成代码盘点，再冻结视觉与技术方案 | 避免设计出与 Avalonia 12、Semi.Avalonia 或现有服务结构冲突的方案 |
| 动态主题必须带可读性约束与稳定降级 | 壁纸颜色不可控，不能让文字和操作状态失去对比度 |
| 壁纸复制到应用数据目录 | 原图移动或删除后主题仍可恢复，权限与路径行为可控 |
| 新增 McKuro 语义资源而非全面覆盖 Semi 资源 | 降低主题升级风险并明确玻璃、遮罩、壁纸文字的职责 |
| 只模糊一层全局壁纸 | 避免为大量卡片重复执行昂贵的实时模糊 |
| 液态玻璃只用于少量交互焦点 | 保持层级清晰，避免所有区域都透明发亮造成视觉噪声 |
| 视频全屏接入启动页并随包提供原生 VLC | 同时修复 UI 回归和无系统 VLC 时无法播放两个根因 |
| 跨平台视觉只依赖 Avalonia 内部渲染 | Mica/Acrylic/合成器模糊在三平台能力不同，不能作为功能前提 |
| 平台专属功能经 Capability/Adapter 隔离 | 防止 Windows `.exe`、路径和安装逻辑渗入跨平台 ViewModel |
| macOS 首要支持 Apple Silicon | 官方游戏要求 macOS 12+ 与 M1+，与 `osx-arm64` 发布目标一致 |
| Linux 游戏启动为显式配置、非保证能力 | 官方原生 Linux 支持未确认且存在内核级反作弊，不实现绕过 |

## Acceptance Criteria
- 用户选择壁纸后 1 秒内看到预览；重启后仍生效；恢复默认可用。
- 明亮、暗色、低饱和和高饱和壁纸下，正文、导航和主要按钮均清晰可读。
- 切换壁纸时强调色、导航选中态、进度条和玻璃色调同步更新，无需重启。
- 普通页面没有大面积硬编码 `SemiColorBg2/Bg3` 卡片墙，视觉层级由背景、GlassSurface 和 Dense 表面构成。
- 关闭玻璃或透明效果不可用时，界面自动变为稳定的不透明主题，不出现透明黑块或文字丢失。
- 干净 Windows 环境无需另装 VLC 即可播放当前官方 MP4；播放失败时静态首帧可见且应用无崩溃。
- macOS Apple Silicon 和 Linux x64 能完成启动、导航、壁纸切换、主题生成、账号/签到/图鉴等非 Windows 专属流程。
- macOS 能识别或打开官方 App Store 游戏应用；更新按钮转交 App Store。
- Linux 未配置游戏兼容层时明确显示“数据功能可用、游戏启动未配置”，不会搜索或执行 Windows `.exe`。
- 三个平台缺少系统透明或媒体能力时都能降级为不透明表面/静态首帧，不出现崩溃。
- 视频导航离开后停止并释放，重新进入能再次播放；下载/安装等核心功能不受影响。

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| `git show` 参数顺序错误导致无输出 | 1 | 改为将提交号放在路径分隔符 `--` 前 |
| 查询 `.github` 失败：仓库没有该目录 | 1 | 记录为发布基础设施缺口，计划新增跨平台 CI 工作流 |

## Notes
- 本轮只做方案与代码定位，不修改业务实现。
- 所有玻璃效果都必须保留关闭透明效果的降级路径。
