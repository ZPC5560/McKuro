# McKuro · 鸣潮启动器

基于 **.NET 10 + Avalonia 12 + Semi Design** 的《鸣潮》(Wuthering Waves)桌面启动器,支持原生 AOT 发布。

整合参考项目:

- [Haiyu](https://github.com/HaiyuGame/Haiyu) — 抽卡分析算法(保底/歪率/欧气评分)、卡池 UP 数据
- [WutheringWavesTool](https://github.com/leck995/WutheringWavesTool) — 日志解密、抽卡接口、本地数据解析

## 功能

| 模块 | 说明 |
| --- | --- |
| 🚀 启动器 | 检查更新(版本数值比较,对齐 Haiyu)、**预下载**(不影响已安装文件,支持**暂停/继续**)、安装更新、**修复游戏**(跳过校验文件,对齐 Haiyu)、**下载速度限制**(MB/s)、**启动参数**(DX11/DLSS/自定义参数/可选启动文件,对齐 Haiyu StartGameOption)、**启动游戏后最小化主窗口**、**DLSS/XeSS 版本检测显示**、打开游戏目录;**选目录后自动识别加载**(校验 exe/渠道/版本,自动检查更新);官方封面轮播 + 公告/活动/新闻面板 + 背景视频封面(可选,libmpv 软件渲染;无 native 库自动回退官方首帧图)+ 版本 Logo |
| 🎨 动态主题 | 主页可更换 PNG/JPG/WebP 壁纸;壁纸主色驱动强调色、导航选中态和玻璃表面;普通数据页使用单实例全局轻模糊,低性能模式自动关闭模糊;壁纸复制到应用数据目录后不依赖原文件 |
| 🏠 主页 | Welcome 欢迎页 + **角色资料卡**(库街区头像/昵称/等级/**游玩天数**/注册日期;**开服玩家徽章**——2024-05-23 开服及后 10 天内注册即显示)与**每日数据网格**(体力/结晶单质/活跃度/**战歌重奏·周本**(官方周本 Logo,对齐 Java WutheringWavesTool)/终焉矩阵/冥歌海墟/千道门扉/周度游历/**电台**(等级 + 经验进度 "经验: 3250/12000"));本地 PC 启动器 SDK 与库街区(数据中心 baseData 补齐资料)双数据源,图标按需懒加载 |
| 🎴 抽卡分析 | 解析 `Client.log` 解密 URL → 从官方接口拉取抽卡记录;**双通道同步(云鸣潮接口优先,本地日志回退)**;按卡池统计保底/当前垫抽/小保底歪率/欧气评分/称号/双金/歪数/平均出货/出货率/天数;标注是否 UP;**多账号切换与「全部账号」聚合分析**;**五星角色/武器真实头像图标**;本地 SQLite 去重存储;Haiyu 风格五星列表 + 统计条 + 饼图/折线图 |
| 👤 角色数据 | 通过**库街区** API 或 **mcguide 攻略站**双数据源拉取当前账号角色数据(等级/武器/技能/共鸣链/**声骸/属性面板**),**网格卡片原生展示**(WutheringWavesTool 风格);图标磁盘持久化缓存(6 类,切换数据源不丢图);也支持解析游戏本地缓存;Echo 评分 |
| 📅 签到 | 库街区账号登录(**Token** / **手机号+验证码**:极验人机验证 → 发送验证码 → 60s 倒计时重发 → 登录,流程对齐 Haiyu)、**一键游戏签到(鸣潮全部角色)**、**库街区每日任务**(库洛币签到+浏览+点赞+分享)、每日 8:00 自动签到 |
| ☁️ 云游戏 | 云鸣潮 SDK 服务(手机号登录/节点测速/启动排队,对齐 Haiyu WavesCloudGameService);当前 UI 通过其会话用于**抽卡分析云鸣潮双通道**拉取记录 |
| 📖 图鉴 | 库街区 wiki 首页数据(Banner 轮播/公告/热点/活动,鸣潮)+ 网页快捷入口(官方 wiki/地图、Gamekee/彩墨地图) |
| 📸 快捷键截图 | 设置中保留截图配置字段(开关/修饰键/按键/保存目录,默认 Win+F8),**当前版本尚未实现全局热键截图**(仅为设置项预留) |
| ⚙️ 设置 | 游戏目录(**选择后自动识别加载**)、服务器渠道、**游戏修复·跳过校验文件管理**(添加/移除/是否删除)、库街区 Token/账号管理、下载并发数、**下载限速**、**游戏启动参数**(DX11/DLSS/自定义参数/启动文件)、**启动后最小化**、**动态壁纸/色板/玻璃质量**、**主题切换**(跟随系统/浅色/深色,即时生效)、**应用自更新**(GitHub Release 检查/跳过版本/下载安装,对齐 Haiyu UpdateAppViewModel)、截图配置、界面语言(zh-Hans/en-US) |

应用图标:守岸人(Shorekeeper)官方图标(萌娘共享 CC-BY-NC-SA),多尺寸 ICO 内嵌 exe 与窗口标题栏。

## 环境要求

- .NET SDK 10.0 (`dotnet --version` 应显示 10.x)
- Windows 10/11 运行游戏与完整更新功能;macOS/Linux 提供 UI、壁纸、主题、数据和视频首帧回退(游戏本体仅 Windows;Linux 视频优先使用系统 libmpv)

## 项目结构

```
McKuro/
├── McKuro.slnx                 # 解决方案
├── src/McKuro/                 # Avalonia 桌面应用 (Semi 主题)
│   ├── Views/                 # 主页 / 鸣潮(启动器) / 抽卡分析 / 角色数据 / 签到 / 活动 / 图鉴 / 兑换码 / 游玩统计 / 深塔海墟 / 账号 / 设置
│   ├── ViewModels/            # MVVM (CommunityToolkit.Mvvm)
│   ├── Controls/              # AsyncImage / VideoBackgroundControl(libmpv) / LruCache / RingProgress / SpeedTrendChart
│   ├── Services/              # 手动 DI (AppServices) + 壁纸/色板/平台能力 + 极验 + 每日调度 + 多语言
│   └── Assets/lang/           # zh-Hans / en-US 界面语言资源
├── src/McKuro.Core/            # 与 UI 无关的核心库 (AOT 兼容,源生成 JSON)
│   ├── Services/Gacha/        # 日志解密 / URL 提取 / 接口 / 分析 / 存储 / 云鸣潮双通道
│   ├── Services/Game/         # 清单加载 / 断点下载 / 差分安装(hpatchz) / 更新 / 游玩时长
│   ├── Services/Roles/        # 库街区 API / mcguide 攻略站 / 本地数据读取 / 缓存 / Echo 评分
│   ├── Services/Kuro/         # 库街区登录 / 签到 / 每日任务
│   ├── Services/CloudGame/    # 云鸣潮 SDK 登录 / 节点测速 / 启动排队
│   ├── Services/Wiki/         # 库街区图鉴首页 / 热点 / 活动
│   └── Infrastructure/        # SQLite (Microsoft.Data.Sqlite)
└── tests/McKuro.Tests/         # xUnit 单元测试 (195 个用例)
```

## 常用命令

```bash
dotnet restore                                    # 还原依赖
dotnet build                                      # 构建
dotnet run --project src/McKuro                    # 运行 (开发)
dotnet test                                       # 运行测试
dotnet publish src/McKuro -c Release -r win-x64 --self-contained   # AOT 发布 (Windows 上执行)
dotnet publish src/McKuro -c Release -r osx-arm64 --self-contained # macOS 本地验证 AOT
dotnet publish src/McKuro -c Release -r linux-x64 --self-contained # Linux 本地验证 AOT/UI
```

Windows 发布会按 RID 条件带上 `Endpne.LibMPV.Windows`(libmpv-2.dll),不要求用户另装 VLC。Linux 使用系统 `libmpv`;macOS 和 Linux 找不到可用媒体运行库时,启动器会保留官方静态首帧,不影响其他页面。

## 日志

应用将运行日志写入日志目录(**Windows: exe 所在目录\logs;macOS/Linux: %AppData%\McKuro\logs**),**按类型分目录**(如 `SmsLogin`/`GeetVerifyService`/`GameUpdater`),目录内**按日期分文件**(`McKuro-yyyyMMdd.log`),跨天自动新建当日文件、旧文件保留。内容覆盖极验验证(本地服务端口/页面地址/回调提取结果)、短信验证码发送响应、更新与下载等关键流程,便于本地排查。设置页「打开日志目录」按钮可直达日志根目录。

## 安装包

`installer/setup.iss` 为 Inno Setup 脚本,由 GitHub Actions 的 `setup` job 在发布 tag(v*) 或手动触发时编译为 `McKuro-setup-<version>.exe`(中文/英文向导、桌面快捷方式、卸载)。

```bash
# 本地编译安装包(需安装 Inno Setup 6)
ISCC.exe installer\setup.iss /DMyAppVersion=1.0.0
```

> **AOT 说明**:项目已启用 `PublishAot`。AOT 无法跨平台交叉编译,win-x64 必须在 Windows 上发布;osx-arm64 可在 macOS 本地验证。

## 使用说明

### 抽卡分析

1. 在设置页指定游戏安装目录(或自动检测)
2. 点击「抽卡分析 → 同步」:优先走**云鸣潮接口**(已登录时),失败自动回退解密 `Client/Saved/Logs/Client.log` 提取抽卡记录 URL,从官方接口拉取全部记录;两者都失败时回退本地 SQLite 缓存展示
3. 卡池列表展示各池统计,点击查看五星出货明细(垫抽数/是否 UP)

> UP 标注依赖第三方卡池数据源(可能不可用),不可用时仅展示保底统计,不判定是否 UP。

### 角色养成

1. 登录[库街区](https://www.kurobbs.com)网页版,按 F12 从请求头复制 `token`
2. 在设置页填入 Token 与角色 ID(playerId)
3. 点击「从库街区同步」即可在界面直接查看当前账号的角色养成数据(自动缓存)

### 服务器渠道

- **自动检测**:根据游戏目录内 KRSDK 目录识别(Bilibili/WeGame/Global/Mainland)
- 自动检测失败时可手动指定

## 隐私与数据

- 所有数据保存在本机 `%AppData%/McKuro`(Windows)或 `~/.local/share/McKuro`(macOS/Linux)
- 抽卡记录与角色数据仅用于本地展示,不上传任何服务器
