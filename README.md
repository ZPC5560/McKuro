# donet · 鸣潮启动器

基于 **.NET 10 + Avalonia 12 + Semi Design** 的《鸣潮》(Wuthering Waves)桌面启动器,支持原生 AOT 发布。

整合参考项目:

- [Haiyu](https://github.com/HaiyuGame/Haiyu) — 抽卡分析算法(保底/歪率/欧气评分)、卡池 UP 数据
- [WutheringWavesTool](https://github.com/leck995/WutheringWavesTool) — 日志解密、抽卡接口、本地数据解析

## 功能

| 模块 | 说明 |
| --- | --- |
| 🚀 启动器 | 检查更新、**预下载**(不影响已安装文件)、安装更新、启动游戏、打开游戏目录;官方封面轮播 + 公告/活动/新闻面板 + 背景视频封面(可选,LibVLC;无 VLC 自动回退官方首帧图)+ 版本 Logo |
| 🎴 抽卡分析 | 解析 `Client.log` 解密 URL → 从官方接口拉取抽卡记录;按卡池统计保底/当前垫抽/小保底歪率/欧气评分/称号/双金/歪数/平均出货/出货率/天数;标注是否 UP;**五星角色/武器真实头像图标**;本地 SQLite 去重存储;Haiyu 风格五星列表 + 统计条 + 饼图/折线图 |
| 👤 角色数据 | 通过**库街区** API 直接拉取当前账号角色数据(等级/武器/技能/共鸣链),原生 UI 展示;也支持解析游戏本地缓存 |
| ⚙️ 设置 | 游戏目录、服务器渠道(官服/B站/WeGame/国际服)、库街区 Token、下载并发数 |

## 环境要求

- .NET SDK 10.0 (`dotnet --version` 应显示 10.x)
- Windows 10/11 运行游戏与完整更新功能;macOS/Linux 可编译、测试与开发(游戏本体仅 Windows)

## 项目结构

```
donet/
├── donet.sln                  # 解决方案
├── src/donet/                 # Avalonia 桌面应用 (Semi 主题)
│   ├── Views/                 # 主页 / 鸣潮 / 抽卡分析 / 角色数据 / 工具箱 / 设置 六页 (Haiyu Shell 风格)
│   ├── ViewModels/            # MVVM (CommunityToolkit.Mvvm)
│   └── Services/              # 手动 DI (AppServices)
├── src/donet.Core/            # 与 UI 无关的核心库 (AOT 兼容,源生成 JSON)
│   ├── Services/Gacha/        # 日志解密 / URL 提取 / 接口 / 分析 / 存储
│   ├── Services/Game/         # 清单加载 / 断点下载 / 差分安装 / 更新
│   ├── Services/Roles/        # 库街区 API / 本地数据读取 / 缓存
│   └── Infrastructure/        # SQLite (Microsoft.Data.Sqlite)
└── tests/donet.Tests/         # xUnit 单元测试 (24 个)
```

## 常用命令

```bash
dotnet restore                                    # 还原依赖
dotnet build                                      # 构建
dotnet run --project src/donet                    # 运行 (开发)
dotnet test                                       # 运行测试
dotnet publish src/donet -c Release -r win-x64 --self-contained   # AOT 发布 (Windows 上执行)
dotnet publish src/donet -c Release -r osx-arm64 --self-contained # macOS 本地验证 AOT
```

> **AOT 说明**:项目已启用 `PublishAot`。AOT 无法跨平台交叉编译,win-x64 必须在 Windows 上发布;osx-arm64 可在 macOS 本地验证。

## 使用说明

### 抽卡分析

1. 在设置页指定游戏安装目录(或自动检测)
2. 点击「抽卡分析 → 从日志同步」:工具解密 `Client/Saved/Logs/Client.log`,提取抽卡记录 URL,从官方接口拉取全部记录
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

- 所有数据保存在本机 `%AppData%/donet`(Windows)或 `~/.local/share/donet`(macOS/Linux)
- 抽卡记录与角色数据仅用于本地展示,不上传任何服务器
