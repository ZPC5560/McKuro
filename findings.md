# Findings & Decisions

## Requirements
- 用户可更换主页壁纸。
- 整个应用的主题颜色根据当前壁纸自动变化。
- 没有固定展示内容的页面使用毛玻璃或液态玻璃效果。
- 修复官方视频背景无法播放的问题。
- 新增硬性要求：应用需要支持 Windows、macOS 和桌面 Linux，不能把壁纸、主题、玻璃或视频实现锁死在 Windows。
- 当前阶段已进入业务实现，先落地 UI 基座与视频回退链路。

## Research Findings
- 当前应用主题由 `App.axaml.cs` 根据 `Settings.Current.Theme` 设置 `RequestedThemeVariant`，界面大量使用 `SemiColorBg*`、`SemiColorText*` 和 `SemiColorPrimary` 动态资源，具备集中覆盖主题 Brush 的基础。
- 首页 `HomeView.axaml` 已使用半透明固定颜色，但这些颜色是硬编码的浅蓝/白色，并未形成全局玻璃表面规范。
- 背景视频开关位于核心设置模型 `src/McKuro.Core/Services/Settings/SettingsService.cs`。
- 视频控件为 `src/McKuro/Controls/VideoBackgroundControl.cs`，官方背景数据由 `LauncherInfoService` 获取。
- 项目仅直接引用 `LibVLCSharp.Avalonia`；项目文件注释提到 Windows 原生库可来自 `VideoLAN.LibVLC.Windows`，但当前项目引用中未看到该包，需要进一步核对初始化和运行时部署。
- 大多数功能页仍直接使用不透明 `SemiColorBg2/Bg3`，适合通过语义资源统一迁移，而不是逐个页面硬编码透明颜色。
- 已确认官方视频无法播放的首要原因：`LauncherView.axaml` 只绑定了 `AsyncImage` 到 `BackgroundImageUrl`，从未实例化 `VideoBackgroundControl`；虽然 ViewModel 正确设置了 `BackgroundVideoUrl` 和 `VideoEnabled`，这两个属性没有任何界面消费者。
- `VideoBackgroundControl` 自身已经具备静态图回退、挂载后初始化和基本循环逻辑，但没有播放状态事件、错误日志服务、静音设置、缓冲/超时诊断，也未持有并显式释放 `Media` 对象。
- 官方背景数据模型包含 `BackgroundFileType`（1=图片、2=视频），当前 ViewModel 没有依据类型决定播放方式，而是看到 `BackgroundFile` 就当作视频 URL。
- `LauncherInfoService` 会轮询四个官方 CDN 并获取背景 JSON；异常全部静默，当前 UI 无法区分“接口失败、字段为空、原生 VLC 缺失、解码失败”。
- `App.axaml` 当前只有 `SemiTheme`，没有项目自有的语义颜色层；`App.axaml.cs` 只切换 Light/Dark/Default，不会更改强调色和表面色。
- `MainWindow.axaml` 的窗口与导航栏均使用不透明 Semi 背景，内容页直接覆盖在其上。若希望其他页面呈现玻璃效果，需要把壁纸/氛围背景提升到主窗口壳层，并让内容页根背景透明。
- `HomeView.axaml` 当前是硬编码深蓝渐变、光晕、白色文字与半透明卡片，视觉效果已有雏形，但无法替换壁纸，也不会根据图像亮度和主色调整。
- 首页的动态主题应避免直接改 Semi 包的全部底层资源；更稳妥的是新增 `McKuro*` 语义资源（背景遮罩、GlassSurface、GlassStroke、TextOnWallpaper、Accent 等），必要时只桥接少量 Semi 主色资源。
- `AppSettings` 已通过 JSON 原子写入 `%AppData%/McKuro/settings.json`，新增壁纸路径、壁纸模式、动态主题开关、玻璃强度、视频静音等字段可以沿用现有持久化机制。
- `SettingsViewModel` 已有文件夹选择器和主题即时生效模式，可复用同类交互实现图片选择器、恢复默认和主题预览。
- `AppServices` 是集中式 DI 注册入口，新增 `WallpaperService`、`ThemePaletteService` 和视频诊断/缓存服务应在此注册，避免 ViewModel 直接处理文件和图像算法。
- `AsyncImage` 支持 HTTP URL 和本地文件路径，但错误被静默吞掉；壁纸功能需要明确的加载结果与错误反馈，不能直接把它当作完整的壁纸管理服务。
- 项目已有 `ColorThiefHelper` 和对应测试，可复用其降采样/量化基础；但动态主题还需要补充平均亮度、饱和度筛选、强调色修正、前景对比度和暗/亮模式判定。
- 当前工程未引用 `VideoLAN.LibVLC.Windows`，因此 Windows 机器若没有系统 VLC，`new LibVLC()` 会失败并回退静态图；安装包若宣称开箱即用，应随应用发布原生 LibVLC 运行时。
- 仓库中没有现成 Backdrop/Acrylic/Blur 控件或样式，玻璃效果需要新增语义样式，并先验证 Avalonia 12 在当前渲染后端下可用的模糊能力与性能。
- Avalonia 官方文档确认 Windows 支持 `TransparencyLevelHint` 的 AcrylicBlur（Windows 10 1803+）和 Mica（Windows 11），但这是窗口背板能力；应用内部壁纸上的局部卡片玻璃仍应以半透明材质、描边、噪点/高光和可选模糊组合实现。
- Avalonia 官方建议为透明效果提供不透明 `TransparencyBackgroundFallback`，并指出系统可能因平台、节能模式或合成器限制关闭透明效果。
- Avalonia 的 `BlurEffect` 会模糊元素本身而非天然的背景采样；高半径和大量模糊区域会显著降低帧率。因此 v1 玻璃方案不应给每张卡片堆叠实时 BlurEffect。
- VideoLAN 官方文档说明：目标项目需要安装对应平台的 `VideoLAN.LibVLC.*` 原生包；使用官方原生 NuGet 时可自动定位，也可显式调用 `Core.Initialize()`。当前项目未满足“随包自带 Windows libvlc”这一条件。
- VideoLAN 建议 `EndReached` 后从线程池重新播放，当前控件在事件回调中直接操作播放器，循环策略需要调整。
- Git 历史确认视频功能是回归：提交 `d1ba9c8` 曾在 `LauncherView.axaml` 中绑定 `VideoBackgroundControl`，提交 `387362a` 重构视觉时将其替换为全屏 `AsyncImage`，从此 `BackgroundVideoUrl` 与 `VideoEnabled` 无消费者。
- 旧版本只把视频放在标题卡片内且 `Opacity=0.25`，即便可以播放也不符合“官方视频背景”的预期。修复时应把 `VideoBackgroundControl` 放到启动页全屏背景层，而不是简单恢复旧的小卡片位置。
- 主导航共 12 个页面。建议按背景策略分组：主页使用用户壁纸；鸣潮启动页使用官方视频/首帧；其余数据与设置页面共享壁纸的模糊/遮罩背景，并使用高可读性玻璃表面。
- 设置页已有“背景视频”和“主题”相邻区域，壁纸选择、动态配色、玻璃强度与恢复默认应整合为一个“个性化”分组，避免继续堆叠孤立卡片。
- `MainWindowViewModel` 当前只暴露 `CurrentPage`，没有页面背景模式。可新增只读 `BackgroundMode`（Wallpaper/LauncherMedia）或让启动页自身提供完全覆盖背景；后者改动更小。
- 实测官方官服背景接口当前返回 `backgroundFileType=2`，视频是有效的 HTTPS MP4，HEAD 响应为 200、`video/mp4`、约 6.87 MB，并支持 byte range；官方数据源本身当前可用。
- 当前开发机没有安装系统 VLC，也没有可发现的 `libvlc.dll`。因此即使恢复 XAML 绑定，现有工程在该机器仍会因缺少原生运行时而回退静态图；必须把 Windows libvlc 随应用发布，才能做到开箱即播。
- Avalonia 官方支持 Windows、macOS 和 Linux，但系统级窗口透明能力不同：Windows 支持完整透明层级，macOS 仅支持 `Transparent`，Linux 取决于合成器；原生 Wayland 后端目前没有 KDE blur-behind。因此核心玻璃视觉不能依赖 Mica/Acrylic 等系统背板。
- 应用内壁纸 + 半透明材质 + 单层 `BlurEffect` 属于 Avalonia 自身渲染，可作为跨平台一致基线；Windows Acrylic/Mica 只能作为可选增强。
- Avalonia 提供 `OnPlatform`，可以为平台差异设置不同的透明强度、窗口行为或控件参数，但业务主题资源应保持同一命名和数据流。
- LibVLCSharp 官方列明 Avalonia 支持 Windows、macOS、Linux。Windows 有 `VideoLAN.LibVLC.Windows` 原生 NuGet；macOS 有 `VideoLAN.LibVLC.Mac`，但公开包较旧；Linux 没有官方原生 NuGet，官方指南要求安装系统 libvlc，或通过 `LD_LIBRARY_PATH` 指向自带库。
- macOS 发布必须生成 `.app` bundle，原生动态库需要进入正确目录并参与签名/公证；Linux 发布仍依赖目标机的图形与媒体原生库，必须通过包依赖或 AppImage 类封装明确交付。
- 当前 UI 启动使用 `UsePlatformDetect()`，基础窗口层可以跨平台；代码中没有大量 Win32 P/Invoke，跨平台改造可控。
- 游戏路径和启动链路仍明显以 Windows 为中心：`GamePathResolver` 硬编码 `.exe`、`Win64` 和 Windows 客户端目录，`GameUpdater` 直接启动 Windows 可执行文件。非 Windows 平台必须通过 `PlatformCapabilities` 禁用这些入口，不能让它们运行后才报错。
- `AppUpdateService` 当前只识别并启动 `.exe` 安装器，多平台自更新需要按 RID 选择资产，并分别处理 Windows installer、macOS `.dmg/.zip` 与 Linux `.AppImage/.tar.gz`；第一阶段可先改为各平台跳转 Release 下载页，避免不安全的通用自动安装。
- `LocalGameDailyDataService` 和部分缓存路径依赖 Windows PC 启动器目录；在 macOS/Linux 应自动跳过并回退库街区接口。
- `Environment.SpecialFolder.ApplicationData` 和 `Path.Combine` 已广泛使用，新增壁纸路径应统一基于 `AppServices.AppDataDir`，不要写死 `%AppData%` 文案或反斜杠。
- 当前仓库没有 `.github/workflows`，README 虽提到 GitHub Actions `setup` job，但克隆内容里只有 Windows Inno Setup 脚本；多平台发布需要从零补齐 CI 矩阵和产物打包。
- 当前发布命令只覆盖 `win-x64` 和 `osx-arm64`，没有 `osx-x64`、`linux-x64` 或对应安装包/压缩包流程。
- 当前官方 App Store 明确提供 Apple Silicon macOS 版本（macOS 12+、M1+），因此 macOS 不应只做“UI 能打开”，应规划独立的 `MacGamePlatformAdapter` 来识别 `.app`/App Store 安装并启动游戏；更新仍交由 App Store，McKuro 不替换商店更新机制。
- Steam 官方页面显示 PC 版使用内核级反作弊。未查到官方原生 Linux 发行支持，因此 Linux 版 McKuro 的基线是 UI、账号、签到、抽卡分析、图鉴、动态主题和视频可用；游戏启动仅提供用户显式配置的 Steam/兼容层入口，不承诺修复或绕过反作弊。

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| 使用现有 Avalonia + Semi.Avalonia 技术栈 | 避免引入另一套 UI 框架并保留项目结构 |
| 视觉设计遵循 frontend-skill 的层级、克制用色与可读性原则 | 动态主题和玻璃效果不能牺牲操作清晰度 |
| 视频修复先接通现有控件绑定，再增强播放生命周期 | 当前最直接的断点是控件根本没有进入视觉树 |
| 壁纸复制到应用数据目录并缓存色板 | 保证重启可靠、避免原文件移动失效，并减少重复取色成本 |
| 壁纸控制色相，Light/Dark 控制表面明暗 | 保留用户主题偏好，同时实现壁纸联动 |
| 全局只使用一个模糊背景层 | 局部卡片主要靠材质、描边和高光表达玻璃，控制 GPU 开销 |
| 启动页媒体独立于用户壁纸 | 官方视频属于固定展示内容，应保持品牌素材完整，不受用户壁纸干扰 |
| 跨平台基线不使用系统 Acrylic/Mica | macOS/Linux 的系统透明能力不一致，应用内部材质才能保持视觉一致 |
| 视频播放器增加平台原生库解析层 | Windows、macOS、Linux 的 libvlc 获取和定位方式不同，不能在控件里硬编码 Windows 路径 |
| 引入 PlatformCapabilities 并在 UI 层展示能力状态 | 非 Windows 平台不能暴露必然失败的 `.exe` 启动、Windows 本地缓存和安装器动作 |
| 多平台自更新先采用下载页策略 | 各平台安装格式、签名和权限不同，统一自动执行安装器风险过高 |
| macOS 游戏更新交给 App Store | 官方 macOS 版本通过商店分发，第三方启动器不应修改商店管理的应用内容 |
| Linux 不实现反作弊绕过 | 只提供合法、显式配置的启动入口，失败时给出能力说明，不修改游戏安全机制 |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| 当前机器没有系统 VLC/libvlc | 方案中加入 Windows 原生 LibVLC NuGet 与发布产物校验 |
| Linux 没有官方 LibVLC 原生 NuGet | Linux 包声明系统依赖，后续可增加 AppImage 内置运行时；缺失时静态图降级 |
| macOS 官方 LibVLC NuGet较旧 | 实施前验证与 LibVLCSharp 3.x 的实际兼容性；优先评估在 `.app` 内捆绑受支持的 LibVLC 3.x |
| 仓库没有 `.github/workflows` | 新建按操作系统分 runner 的构建、测试和发布矩阵 |

## Resources
- `src/McKuro/App.axaml`
- `src/McKuro/MainWindow.axaml`
- `src/McKuro/Views/HomeView.axaml`
- `src/McKuro/Controls/VideoBackgroundControl.cs`
- `src/McKuro.Core/Services/Settings/SettingsService.cs`
- Avalonia Windows transparency: https://docs.avaloniaui.net/docs/platform-specific-guides/windows
- Avalonia effects: https://docs.avaloniaui.net/docs/graphics-animation/effects
- LibVLCSharp getting started: https://docs.videolan.me/libvlcsharp/docs/getting_started.html
- LibVLCSharp Core.Initialize: https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.Core.html
- Avalonia macOS: https://docs.avaloniaui.net/docs/platform-specific-guides/macos
- Avalonia Linux: https://docs.avaloniaui.net/docs/platform-specific-guides/linux
- Avalonia platform-specific XAML: https://docs.avaloniaui.net/docs/platform-specific-guides/xaml
- LibVLCSharp Linux setup: https://docs.videolan.me/libvlcsharp/docs/linux-setup.html
- LibVLCSharp supported platforms: https://www.nuget.org/packages/LibVLCSharp
- Wuthering Waves macOS App Store: https://apps.apple.com/us/app/wuthering-waves-cyberpunk/id6475033368?platform=mac
- Wuthering Waves Steam: https://store.steampowered.com/app/3513350/Wuthering_Waves/

## Visual/Browser Findings
- 尚未运行应用或检查截图。
