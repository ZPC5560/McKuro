# McKuro · 鸣潮启动器

基于 **.NET 10 + Avalonia 12 + Semi Design** 的《鸣潮》(Wuthering Waves)桌面启动器,支持原生 AOT 发布。当前版本 **1.1.0**。

## 功能

| 模块 | 说明 |
| --- | --- |
| 主页 | 欢迎页 + 角色资料卡(头像/昵称/角色 ID/等级/游玩天数/注册日期,**开服玩家徽章**)与每日数据网格(体力/结晶单质/活跃度/周本/终焉矩阵/冥歌海墟/千道门扉/周度游历/电台经验进度,体力与结晶单质带**恢复满倒计时**);本地 PC 启动器 SDK 与库街区双数据源,图标按需懒加载 |
| 启动器 | 检查更新(版本数值比较)、**预下载**(不影响已安装文件,支持暂停/继续)、安装更新、**修复游戏**(跳过校验文件)、**下载速度限制**(MB/s)、**启动参数**(DX11/DLSS/自定义参数/可选启动文件)、**DLSS/XeSS 版本检测**、打开游戏目录;选目录后自动识别加载;官方封面轮播 + 游戏公告(带封面)+ **背景视频封面**(可选,libmpv 软件渲染,无 native 库自动回退官方首帧图);**启动按钮三态(启动游戏/启动中/游戏中)+ 进程监控**;**启动后最小化位置可选任务栏/系统托盘**;**游戏结束后窗口状态可配**;主窗口纵横比跟随启动页视频,拖动全程锁比例 |
| 系统材质外观 | 导航栏/内容背景跟随操作系统原生桌面材质:**Windows 11 Mica / Windows 10 Acrylic / macOS 毛玻璃·液态玻璃 / Linux 透明背景+应用染色**;主题切换(跟随系统/浅色/深色,即时生效);导航栏为**液态玻璃质感**,选中项用 Apple 液态玻璃**滑动胶囊**,顶部显示库街区**真实账号头像** |
| 抽卡分析 | 解析 `Client.log` 解密 URL → 官方接口拉取记录;**双通道同步(云鸣潮接口优先,本地日志回退)**;按卡池统计保底/当前垫抽/小保底歪率/欧气评分/称号/双金/歪数/平均出货/出货率/天数;**逐条五星记录 UP/歪标注(常驻武器池等不可判定池不误标)**;**环形统计图**(下拉切换)+ **每日抽数平滑面积图(悬停查看日期/卡池/抽数)**;多账号切换与「全部账号」聚合分析;五星角色/武器真实头像图标;本地 SQLite 去重存储 |
| 角色数据 | **库街区** API 或 **mcguide 攻略站**双数据源拉取角色数据(等级/武器/技能/共鸣链/声骸/属性面板),网格卡片原生展示;图标磁盘持久化缓存(6 类,切换数据源不丢图);**库街区被极验风控时优先展示完整缓存**;也支持解析游戏本地缓存;Echo 评分 |
| 签到 | 一键游戏签到(鸣潮全部角色)、签到奖励与签到统计、每日 8:00 自动签到;**多账号签到**(遍历全部库街区账号拉取全部角色,角色头像走本地缓存) |
| 活动 | 官方活动甘特图(当前版本活动,过期剔除,活动图自动取主色) |
| 资讯 | 库街区 wiki 首页数据(Banner 轮播/官方资讯/热点)+ 游戏公告卡(带封面)+ 网页快捷入口(官方 wiki/地图、Gamekee/彩墨地图) |
| 兑换码 | 远程拉取鸣潮兑换码清单(国服/国际服,有效在前),一键复制 |
| 游玩统计 | 最近 7 天逐日游玩时长 + 7×24 时段热力格 |
| 深塔海墟 | 三个页签:**逆境深塔 / 终焉矩阵 / 再生海域**,解析对齐 Java 版 WutheringWavesTool;**终焉矩阵往期历史本地持久化**;进入页面自动刷新 |
| 账号 | **全部接口账号登录的统一入口**:库街区多账号(短信+极验登录/切换/移除)、云鸣潮登录、mcguide 官方评级登录,三张登录卡**堆叠展示**;库街区短信验证用**应用内极验窗口**(macOS=WKWebView / Windows=WebView2);**同一账号自动判定**(手机号比对,仅提醒不强制登出);各接口标签头带**登录状态点**(绿/橙/灰) |
| 设置 | 游戏目录(自动识别加载)、服务器渠道、游戏修复·跳过校验文件管理、下载并发/限速、游戏启动参数、**启动后最小化位置(任务栏/托盘)**、**游戏结束后窗口状态**、**启动时打开的页面(主页/启动页)**、主题/背景视频封面、**应用自更新**(GitHub Release 检查/跳过版本;**优先 zip 绿色包解压替换,exe 安装包静默自动更新**)、界面语言(zh-Hans/en-US) |

应用图标:守岸人(Shorekeeper)官方图标(萌娘共享 CC-BY-NC-SA),多尺寸 ICO 内嵌 exe、窗口标题栏与系统托盘。

## 环境要求

- .NET SDK 10.0 (`dotnet --version` 应显示 10.x)
- Windows 10/11 运行游戏与完整更新功能;macOS/Linux 提供 UI、主题材质、数据和视频首帧回退(游戏本体仅 Windows;Linux 视频优先使用系统 libmpv)

## 项目结构

```
McKuro/
├── McKuro.slnx                 # 解决方案
├── src/McKuro/                 # Avalonia 桌面应用 (Semi 主题)
│   ├── Views/                 # 主页 / 鸣潮(启动器) / 抽卡分析 / 角色数据 / 签到 / 活动 / 资讯 / 兑换码 / 游玩统计 / 深塔海墟 / 账号 / 设置
│   ├── ViewModels/            # MVVM (CommunityToolkit.Mvvm)
│   ├── Controls/              # AsyncImage / VideoBackgroundControl(libmpv) / TimeLineChart / SpeedTrendChart / RingProgress / LruCache / WebView2Control(Windows 极验) / WkWebViewControl(macOS 极验)
│   ├── Services/              # 手动 DI (AppServices) + 系统材质 / 游戏进程监控 / 极验 / 每日调度 / 多语言
│   └── Assets/lang/           # zh-Hans / en-US 界面语言资源
├── src/McKuro.Core/            # 与 UI 无关的核心库 (AOT 兼容,源生成 JSON)
│   ├── Services/Gacha/        # 日志解密 / URL 提取 / 接口 / 分析 / 存储 / 云鸣潮双通道
│   ├── Services/Game/         # 清单加载 / 断点下载 / 差分安装(hpatchz) / 更新 / 游玩时长
│   ├── Services/Roles/        # 库街区 API / mcguide 攻略站 / 本地数据读取 / 缓存 / Echo 评分
│   ├── Services/Kuro/         # 库街区登录 / 签到 / 每日任务
│   ├── Services/CloudGame/    # 云鸣潮 SDK 登录 / 节点测速 / 启动排队
│   ├── Services/Tower/        # 逆境深塔 / 终焉矩阵 / 再生海域
│   ├── Services/Wiki/         # 库街区资讯首页 / 热点 / 公告
│   └── Infrastructure/        # SQLite (Microsoft.Data.Sqlite)
└── tests/McKuro.Tests/         # xUnit 单元测试 (365 个用例)
```

## 常用命令

```bash
dotnet restore                                    # 还原依赖
dotnet build McKuro.slnx -c Release               # 构建(解决方案级请显式 -c Release)
dotnet run --project src/McKuro                    # 运行(项目级构建默认 Release,见 Directory.Build.props)
dotnet test McKuro.slnx -c Release                # 运行测试
dotnet publish src/McKuro -c Release -r win-x64 --self-contained   # AOT 发布 (Windows 上执行)
dotnet publish src/McKuro -c Release -r osx-arm64 --self-contained # macOS 本地验证 AOT
dotnet publish src/McKuro -c Release -r linux-x64 --self-contained # Linux 本地验证 AOT/UI
```

> **构建配置**:项目级构建(`dotnet run --project src/McKuro` 等)由 `Directory.Build.props` 默认 Release;解决方案级构建(`dotnet build`/`dotnet test` 不带项目路径)SDK 会默认注入 Debug,**请显式加 `-c Release`**(`McKuro.slnx` 已声明 `Release|AnyCPU` 映射)。

Windows 发布会按 RID 条件带上 `Endpne.LibMPV.Windows`(libmpv-2.dll),不要求用户另装 VLC。Linux 使用系统 `libmpv`;macOS 和 Linux 找不到可用媒体运行库时,启动器会保留官方静态首帧,不影响其他页面。

## 日志

应用将运行日志写入日志目录(**Windows: exe 所在目录\logs;macOS/Linux: %AppData%\McKuro\logs**),**按类型分目录**(如 `SmsLogin`/`GeetVerifyService`/`GameUpdater`),目录内**按日期分文件**(`McKuro-yyyyMMdd.log`),跨天自动新建当日文件、旧文件保留。内容覆盖极验验证(本地服务端口/页面地址/回调提取结果)、短信验证码发送响应、更新与下载等关键流程,便于本地排查。设置页「打开日志目录」按钮可直达日志根目录。

## 安装包

`installer/setup.iss` 为 Inno Setup 脚本,由 GitHub Actions 的 `setup` job 在发布 tag(v*) 或手动触发时编译为 `McKuro-setup-<version>.exe`(中文/英文向导、桌面快捷方式、卸载)。

**应用自更新**:设置页检查 GitHub Release(默认仓库 `ZPC5560/McKuro`),**优先下载 zip 绿色包**(解压替换安装目录并重启);exe 安装包走**静默自动更新**(`/VERYSILENT` + `/DIR` 锁定当前目录,目录可写时 `/CURRENTUSER` 免 UAC,安装完由监视脚本自动拉起新版),全程零向导。**启动后自动检查更新**(默认开,延迟 5 秒静默检查):发现新版可**自动下载安装并重启**(零点击,默认关)或弹窗询问(立即更新/稍后/跳过此版本);手动双击 setup.exe 时自动定位既有安装目录(应用启动自注册 `HKCU\Software\McKuro\InstallPath`,覆盖无卸载注册表项的 zip 便携版);支持跳过指定版本。

```bash
# 本地编译安装包(需安装 Inno Setup 6)
ISCC.exe installer\setup.iss /DMyAppVersion=1.1.0
```

> **AOT 说明**:项目已启用 `PublishAot`。AOT 无法跨平台交叉编译,win-x64 必须在 Windows 上发布;osx-arm64 可在 macOS 本地验证。

## 使用说明

### 账号登录

所有接口账号(库街区/云鸣潮/mcguide)统一在**「账号」页**登录与管理:三张登录卡堆叠展示,支持 Token、手机号+验证码(库街区短信验证在**应用内极验窗口**完成)等方式;已登录多个接口时自动按手机号判定是否同一账号(仅提醒,不强制登出)。

### 抽卡分析

1. 在「账号」页登录库街区或云鸣潮(未登录时走本地日志通道)
2. 点击「抽卡分析 → 同步」:优先走**云鸣潮接口**,失败自动回退解密 `Client/Saved/Logs/Client.log` 提取抽卡记录 URL,从官方接口拉取全部记录;两者都失败时回退本地 SQLite 缓存展示
3. 卡池列表展示各池统计,点击查看五星出货明细(垫抽数/是否 UP)

> UP 标注依赖第三方卡池数据源(可能不可用),不可用时仅展示保底统计;常驻武器池等不可判定池不显示 UP/歪 徽章。

### 角色养成

1. 在「账号」页登录库街区(或填入 Token 与角色 ID)
2. 点击「从库街区同步」即可在界面直接查看当前账号的角色养成数据(自动缓存,数据源可切换 mcguide 攻略站)

### 服务器渠道

- **自动检测**:根据游戏目录内 KRSDK 目录识别(Bilibili/WeGame/Global/Mainland)
- 自动检测失败时可手动指定

## 隐私与数据

- 所有数据保存在本机 `%AppData%/McKuro`(Windows)或 `~/.local/share/McKuro`(macOS/Linux)
- 抽卡记录与角色数据仅用于本地展示,不上传任何服务器

## 特别鸣谢

本项目的部分功能与实现参考了以下开源项目,在此特别感谢:

- [Haiyu](https://github.com/HaiyuGame/Haiyu) — 抽卡分析算法(保底/歪率/欧气评分)、卡池 UP 数据、启动器交互(进程监控/最小化位置/游戏结束窗口状态)
- [WutheringWavesTool](https://github.com/leck995/WutheringWavesTool) — 日志解密、抽卡接口、本地数据解析、深塔/终焉矩阵/海墟解析
