# McKuro · Wuthering Waves Launcher

[![Release](https://img.shields.io/github/v/release/ZPC5560/McKuro?label=Release&color=blue)](https://github.com/ZPC5560/McKuro/releases/latest)
[![CI](https://img.shields.io/github/actions/workflow/status/ZPC5560/McKuro/build-and-test.yml?branch=main&label=CI)](https://github.com/ZPC5560/McKuro/actions/workflows/build-and-test.yml)
![.NET](https://img.shields.io/badge/.NET-10-5C2D91) ![Avalonia](https://img.shields.io/badge/Avalonia-12.1-8B44AC) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**[简体中文](README.md)** · English

A desktop launcher for *Wuthering Waves*, built with **.NET 10 + Avalonia 12 + Semi Design**, published as a **Native AOT** binary — **all data stays on your machine**.

**[Website](https://zpc5560.github.io/McKuro/)** · **[Download](https://github.com/ZPC5560/McKuro/releases/latest)** · **[Changelog](CHANGELOG.md)**

## Feature Overview

| Page | One-liner |
| --- | --- |
| Home | Account profile card (incl. "day-one player" badge) + daily stats grid (Stamina / Crystallize Bundle with full-recovery countdown), dual data sources, Live2D showcase (Windows x64) |
| Launcher | Update check, pre-download, streaming install, repair, speed limit, launch options, DLSS/XeSS detection; libmpv GPU background video and custom animated wallpaper; tri-state start button + process monitor |
| System Materials | Nav/content background follows native desktop materials: Win11 Mica / Win10 Acrylic / macOS frosted & liquid glass / Linux transparent tinting; liquid-glass navigation with sliding pill |
| Gacha Analysis | Dual-channel sync (Cloud API first, local log fallback); per-banner stats (pity, soft-pity miss rate, luck score, and more); ring chart + daily-pull area chart; multi-account aggregation, deduplicated in local SQLite |
| Characters | Development panels (level/weapon/skills/chain/echo/stats) from KuroBBS or the Official Guide Site; disk-persistent icon cache; Echo rating |
| Daily Sign-in | One-click sign-in for all characters, automatic sign-in at 08:00 daily, multi-account sweep |
| Events | Official event Gantt chart: expired events pruned, even date ticks, today-line pixel-aligned, auto-scrolled to today |
| Notifications | Five kinds of floating reminders (sign-in / event ending / session expired / weekly bosses / daily activity), each with an independent toggle and per-day dedup persistence |
| News | KuroBBS wiki front page + announcement cards + web shortcuts; multi-language PV auto-selects the English part; links switch to global official sources in English UI |
| Redeem Codes | Remote code list (CN/global, active first), one-click copy per card with a floating success toast |
| Play Stats | Daily play time for the last 7 days + 7×24 hour heat grid |
| Tower & Wastes | Three tabs — Tower of Adversity / Mortal Matrix / Whimpering Wastes; past history persisted locally (with start→end ranges); per-team scores by round for Mortal Matrix |
| Account | Single entry point for KuroBBS multi-accounts / Cloud Wuthering Waves / Official Guide Site sign-in; in-app CAPTCHA window; same-account auto-detection |
| Settings | Game directory auto-detect, download concurrency/speed limit, launch & minimize behavior, background video source, in-app self-update, full zh-Hans / en-US UI switching |

App icon: official Shorekeeper icon (shared under CC BY-NC-SA), multi-size ICO embedded in the exe, window title bar and system tray.

## Feature Highlights

### Game Launch & Updates

- **Streaming install**: full updates and repairs run "download while applying", with parallel file-level verification, automatic backup before replacement, and no extra staging-disk copy; **repair** can skip file verification.
- **Pre-download** does not touch installed files (pause/resume supported); **download speed limit** (MB/s) and concurrency are adjustable.
- **Resource levels**: HD/SD/UHD switching computes a diff pack against what's already installed and downloads only the missing parts.
- **Launch options**: DX11/DLSS toggles, custom arguments, optional launch file; **DLSS/XeSS version detection**; one-click open game folder; directory auto-detect after selection.
- **Process monitor**: tri-state start button (Start / Starting / In-game); minimize to taskbar or system tray after launch; post-game window behavior configurable.

### Visuals & Wallpaper

- **Background video**: libmpv GPU rendering (OpenGL + hardware decode; CPU usage ≈ 1/6 of software rendering); falls back to software rendering when GL is unavailable, and to the official static first frame when no native library is present.
- **Custom animated wallpaper**: local video files, or auto-scanned Wallpaper Engine video wallpapers (cover previews + live settings preview); when active, official cover/poem overlays hide automatically; paused when the window is minimized or the game is running.
- Official cover carousel + in-game announcements (with covers); the main window aspect ratio follows the launcher video and stays locked while resizing.

### System Integration

- Navigation/content backgrounds follow the OS native material: **Windows 11 Mica / Windows 10 Acrylic / macOS frosted & liquid glass / Linux transparent background + app tinting**; theme switching (system/light/dark) applies instantly.
- Liquid-glass navigation bar; the selected item uses an Apple-style **sliding pill**; the top of the nav shows the real KuroBBS account avatar.

### Data Analysis

- **Gacha Analysis**: decrypts `Client.log` to extract record URLs → pulls history from the official endpoint. **Dual-channel sync** (Cloud Wuthering Waves API first, local log fallback, SQLite cache as last resort). Per-banner pity / current ceiling / soft-pity miss rate / luck score / title / double-5★ / off-banner count / average pulls / rate / days; **per-5★ UP/off-banner tagging** (undeterminable banners such as the standard weapon banner are never mislabeled); **ring chart** (banner switcher) + **smoothed daily-pull area chart** (hover for date/banner/count); multi-account switching with an "all accounts" aggregate; real 5★ character/weapon icons; deduplicated local SQLite storage.
- **Characters**: KuroBBS API or Official Guide Site (level/weapon/skills/chain/echo/stats), native grid cards; icon cache persisted on disk (6 categories, survives data-source switches); when KuroBBS hits GeeTest risk control the complete cache is shown first; local game cache parsing; Echo rating.
- **Tower & Wastes**: three tabs — Tower of Adversity / Mortal Matrix / Whimpering Wastes — parsed to match the Java project [WutheringWavesTool](https://github.com/leck995/WutheringWavesTool); stable two-column area cards; past Mortal Matrix history persisted locally with start→end ranges (start dates back-derived from the season cycle); auto-refresh on entry; per-round team scores (per-team points/progress/characters/buff plus round totals, score and portraits vertically aligned); ratings rendered as the official 6 tiers (SSS/SS/S/A/B/C).
- **Play Stats**: daily play time for the last 7 days + a 7×24 hour heat grid.

### Accounts & Notifications

- The **Account page** is the single entry point for every API login: KuroBBS multi-accounts (SMS + GeeTest sign-in / switch / remove), Cloud Wuthering Waves, and Official Guide Site rating sign-in — three stacked login cards. SMS verification runs in an **in-app CAPTCHA window** (macOS = WKWebView / Windows = WebView2). **Same-account auto-detection** across interfaces via phone number (advisory only, never forces sign-out); each tab header carries a login status dot (green/orange/grey).
- **Five floating reminders** (pop up globally outside the Settings page, auto-dismiss after 8 s, with "Go" jump or manual close): unfinished daily sign-in, event ending soon (≤3 days), account session expired (KuroBBS / Cloud / Guide Site), weekly bosses wrap-up, daily activity. Each category has an independent toggle; **deduplicated per day and persisted** (restarting the same day won't nag twice); the first check runs 15 s after launch, and sign-in-related reminders wait for the auto sign-in to finish to avoid false positives.

### Sign-in · Events · News · Codes

- One-click in-game sign-in (all characters), rewards and statistics, automatic sign-in at 08:00 daily; **multi-account sign-in** sweeps every KuroBBS account and all its characters (character icons served from the local cache).
- Official event Gantt chart: current-version events, expired pruned, event images auto-colored from their primary color; **even date ticks** on the timeline (a date reference is always visible at any scroll position), today line / progress divider pixel-aligned, auto-scrolled to today on entry, full history scrollable.
- News page: KuroBBS wiki front data (banner carousel / official news / hot topics) + announcement cards (with covers) + web shortcuts (official wiki/map, Gamekee/彩墨 map); the Bilibili multi-language PV carousel auto-plays the **English part** (queries the part API under the English UI and picks the [EN] part, falling back to part 1); English UI switches quick links to global official sources (official news / X / YouTube).
- Redeem codes: remote list of active codes (CN/global, active first), **one-click copy per card** writing to the clipboard with a 3 s success toast; the English UI displays known entries via the official term glossary (Astrite, etc.), preserving the original text for unknown items.

### Internationalization & Self-Update

- Full **zh-Hans / en-US** UI switching (841-key bilingual resources covering every page and dynamic message; applied on restart).
- Built-in GitHub Release self-update with channel fallback, resumable downloads and integrity verification — see [Self-Update](#self-update).

## Requirements

- .NET SDK 10.0 (`dotnet --version` should report 10.x)
- Windows 10/11 for running the game and full update features; macOS/Linux provide the UI, theme materials, data pages and static-video fallback (the game itself runs on Windows only; on Linux, video prefers the system libmpv)

## Quick Start

```bash
dotnet restore                                    # restore dependencies
dotnet build McKuro.slnx -c Release               # solution builds need an explicit -c Release
dotnet run --project src/McKuro                   # run (project-level builds default to Release, see Directory.Build.props)
dotnet test McKuro.slnx -c Release                # run tests
dotnet publish src/McKuro -c Release -r win-x64 --self-contained    # AOT publish (run on Windows)
dotnet publish src/McKuro -c Release -r osx-arm64 --self-contained  # macOS local AOT check
dotnet publish src/McKuro -c Release -r linux-x64 --self-contained  # Linux local AOT/UI check
```

> **Build configuration**: project-level builds (`dotnet run --project src/McKuro`, etc.) default to Release via `Directory.Build.props`; solution-level builds (`dotnet build`/`dotnet test` without a project path) get `Debug` injected by the SDK — **always pass `-c Release` explicitly** (`McKuro.slnx` declares the `Release|AnyCPU` mapping).
>
> **AOT note**: `PublishAot` is enabled. AOT cannot cross-compile: win-x64 must be published on Windows; osx-arm64 can be verified locally on macOS.

Windows publishes include `Endpne.LibMPV.Windows` (libmpv-2.dll) conditionally per RID — no separate VLC install needed. Linux uses the system `libmpv`; when no usable media runtime is found on macOS/Linux, the launcher keeps the official static first frame without affecting other pages.

## Project Structure

```
McKuro/
├── McKuro.slnx                     # solution
├── src/McKuro/                     # Avalonia desktop app (Semi theme)
│   ├── Views/                      # Home / Launcher / Gacha / Characters / Sign-in / Events / News / Codes / Play Stats / Tower & Wastes / Account / Settings
│   ├── ViewModels/                 # MVVM (CommunityToolkit.Mvvm)
│   ├── Controls/                   # AsyncImage / VideoBackgroundControl(libmpv) / charts / WebView2·WKWebView (CAPTCHA) / Live2DModelHost
│   ├── Services/                   # manual DI (AppServices) + system materials / game process monitor / daily scheduler / i18n / Live2D runtime lookup
│   └── Assets/lang/                # zh-Hans / en-US UI strings (841 keys)
├── src/McKuro.Core/                # UI-free core library (AOT-friendly, source-generated JSON)
│   ├── Services/Gacha/             # log decryption / URL extraction / endpoints / analysis / storage / cloud dual channel
│   ├── Services/Game/              # manifest loading / resumable download / diff install (hpatchz) / update / play time
│   ├── Services/Roles/             # KuroBBS API / Official Guide Site / local data / cache / Echo rating
│   ├── Services/Kuro/              # KuroBBS login / sign-in / daily tasks
│   ├── Services/CloudGame/         # Cloud Wuthering Waves SDK login / node latency / launch queue
│   ├── Services/Tower/             # Tower of Adversity / Mortal Matrix / Whimpering Wastes
│   ├── Services/Wiki/              # KuroBBS news front / hot topics / announcements
│   ├── Services/                   # Update (self-update) / Launcher / Notification / Redeem / Settings and the rest
│   └── Infrastructure/             # SQLite (Microsoft.Data.Sqlite)
├── src/ThirdParty/Sparkle.Live2DView/  # vendored Live2D rendering control (MIT)
└── tests/McKuro.Tests/             # xUnit unit tests (768 cases)
```

## Usage Guide

### Account Sign-in

Every API account (KuroBBS / Cloud Wuthering Waves / Official Guide Site) signs in and is managed on the **Account page**: three stacked login cards supporting tokens, phone + SMS code (KuroBBS SMS verification completes in the **in-app CAPTCHA window**), etc. With several interfaces signed in, the app auto-detects same accounts by phone number (advisory only, never forces sign-out).

### Gacha Analysis

1. Sign in to KuroBBS or Cloud Wuthering Waves on the Account page (the local log channel is used when signed out).
2. Click **Gacha Analysis → Sync**: the Cloud API is tried first; on failure it falls back to decrypting `Client/Saved/Logs/Client.log` to extract the record URL and pulls the full history from the official endpoint; when both fail, the local SQLite cache is displayed.
3. The banner list shows per-banner stats; click a banner for 5★ details (ceiling count / whether it was on-banner).

> UP tagging depends on a third-party banner schedule source (which may be unavailable). When unavailable only pity statistics are shown; undeterminable banners such as the standard weapon banner never show UP/off-banner badges.

### Character Development

1. Sign in to KuroBBS on the Account page (or provide a token and character UID).
2. Click **Sync from KuroBBS** to view the account's development data directly (auto-cached; the data source can be switched to the Official Guide Site).

### Server Channel

- **Auto-detection**: identified from the KRSDK directory inside the game folder (Bilibili / WeGame / Global / Mainland).
- Manual override available when detection fails.

### Live2D Model (Windows x64 only)

In the **Live2D Model** block of Settings:

1. **Place the Cubism runtime**: `Live2DCubismCore.dll` carries its own Live2D license and is not redistributed — download **Cubism SDK for Native** from the [Live2D website](https://www.live2d.com/sdk/download/native/) and put `Live2DCubismCore.dll` into the directory opened by the "Open Model Directory" button (`%AppData%/McKuro/live2d`) or the app install directory; the status badge should show "ready".
2. **Import a model**: click "Import Model Folder" and pick a folder containing a Cubism 3+ `.model3.json` (scanned recursively), then select the model from the dropdown.
3. **Configure**: scale (0.5–3x) / horizontal / vertical position / opacity sliders; the settings preview supports drag and wheel zoom written back in real time, applied to the Home page instantly.
4. Turn on "Show Live2D model on Home"; the model overlays above the home wallpaper and below the content cards (display-only, never blocks interaction).

Not yet supported on macOS/Linux (the rendering control currently targets Windows x64 only; the settings block is greyed out). Cubism Core and model files have their own license terms — confirm your usage scope.

### UI Language

Settings → **UI language** supports **简体中文 / English**, applied after restart. Bilingual resources (841 keys) cover all page copy, messages and service-layer statuses (the `CoreStrings` gateway: Core resolves strings through a parser registered by the app, falling back to the original Chinese in unit tests when none is registered).

## Releases & Installer

Release tags (`v*`) trigger GitHub Actions to build Native AOT binaries for all platforms and attach them to the Release:

| Asset | Platform | Notes |
| --- | --- | --- |
| `McKuro-setup-<ver>.exe` / `McKuro-win-x64-<ver>.zip` | Windows | Installer compiled from `installer/setup.iss` (Inno Setup): Chinese/English wizard, desktop shortcut, uninstaller |
| `McKuro-osx-arm64-<ver>.zip` / `McKuro-osx-x64-<ver>.zip` | macOS | arm64 pack bundles libmpv — video works out of the box; unsigned, first launch: right-click → Open |
| `McKuro-linux-x64-<ver>.tar.gz` | Linux | Video requires the system libmpv, falling back to the static cover when missing |

For unpacked assets run `chmod +x McKuro` first. To compile the installer locally (requires Inno Setup 6):

```bash
ISCC.exe installer\setup.iss /DMyAppVersion=1.3.2
```

## Self-Update

Settings checks GitHub Releases (default repository `ZPC5560/McKuro`); the update pipeline follows [Haiyu](https://github.com/HaiyuGame/Haiyu)'s design:

- **Check fallback**: GitHub API → GitHub HTML degradation — updates still work when the anonymous API quota is exhausted or `api.github.com` is unreachable; Release results are cached for 5 minutes (auto + manual checks don't double-spend the quota).
- **Download**: resumable (`.part` + Range, auto-retry on stalls); when the publisher provides a digest (Release body or a `<asset>.sha256` asset), **sha256 integrity verification** runs after download and mismatches trigger a discard-and-redownload; without a digest the update is not blocked, and Settings honestly reports whether verification was active.
- **Optional acceleration**: download acceleration template (supports the `{downloadUrl}` placeholder, e.g. `https://gh-proxy.example/{downloadUrl}`); GitHub IP SNI-fronting (off by default, effective immediately, bypasses DNS pollution via a built-in IP table, each IP times out independently after 5 s before the next is tried).
- **Install**: the zip green pack is preferred (unzip in place, then restart); the exe installer runs silently (`/VERYSILENT` + `/DIR` pinned to the current directory, `/CURRENTUSER` without UAC when the directory is writable, a watcher script relaunches the new version), zero wizards throughout.
- **Auto-check**: a silent check runs 5 s after launch (on by default); a new version can auto-download, install and restart (zero clicks, off by default) or ask via dialog (update now / later / skip this version). Double-clicking setup.exe manually locates an existing install (the app self-registers `HKCU\Software\McKuro\InstallPath`, covering zip portable installs without uninstall registry entries).

## Testing & CI

- `tests/McKuro.Tests`: **768 xUnit unit tests** (log decryption, gacha analysis, banner statistics, update pipeline, notifications, i18n, and more).
- `build-and-test.yml`: build + test on Linux for every push/PR; release tags or manual dispatch run the full-platform AOT publishes and attach Release assets.
- `website.yml`: rebuilds and deploys the website whenever `website/` or `website-src/` changes, failing on any drift between built output and committed artifacts.

## Logs

Runtime logs are written to the log directory (**Windows: `<exe dir>\logs`; macOS/Linux: `%AppData%\McKuro\logs`**), organized by category (`SmsLogin`/`GeetVerifyService`/`GameUpdater`, …) with per-date files (`McKuro-yyyyMMdd.log`); a new file starts automatically at day change and old files are kept. Coverage includes CAPTCHA verification, SMS send responses, updates and downloads — the Settings page "Open Log Directory" button jumps straight to the root.

## Website

Project website (GitHub Pages, currently Chinese-only): <https://zpc5560.github.io/McKuro/>

- `website/` holds the committed build output (directly browsable); `website-src/` holds the maintainable sources.
- Sources are split by role: `website-src/html/`, `css/`, `js/`, concatenated in filename order; icons live in `website-src/src/icons/` (Phosphor, MIT), font sources in `website-src/font-src/`.
- Rebuild:

  ```bash
  pip install fonttools brotli
  python website-src/build.py
  ```

  The build subsets the CJK font down to the glyphs actually used (~3.4 MB → ~220 KB) and inlines all icons into an SVG sprite. The pages request no CDN.

- `.github/workflows/website.yml` compares build output with committed artifacts and fails on any mismatch, so the live site can never run a stale copy.

> UI screenshots on the website are **web redraws** using sample data — they contain no real account information.

## Privacy & Data

- All data stays on your machine: `%AppData%/McKuro` (Windows) or `~/.local/share/McKuro` (macOS/Linux).
- Gacha records and character data are only displayed locally and never uploaded to any server.

## License

- McKuro source code is licensed under the **[MIT License](LICENSE)** (Copyright © 2026 ZPC5560).
- Third-party assets keep their original licenses and are not covered by MIT:
  - The app icon (Shorekeeper) is official artwork shared under **CC BY-NC-SA** — non-commercial use only.
  - [Sparkle.Live2DView](https://github.com/Mozi216/Sparkle.Live2DView) and [Live2DCSharpSDK](https://github.com/Coloryr/Live2DCSharpSDK) are **MIT**; `Live2DCubismCore.dll` is under the **Live2D Open Software License** — placed by users, never redistributed in the installers.
  - Website icons (Phosphor) are **MIT**; the bundled `hpatchz` component follows its upstream license (`Assets/HpatchzResource/LICENSE.txt`).
- The names "Wuthering Waves" / 鸣潮, game data endpoints and related content belong to Kuro Games / KuroBBS and their respective rights holders; this project is unaffiliated (see [Disclaimer](#disclaimer)).

## Disclaimer

McKuro is a community-driven open-source project, unaffiliated with and not endorsed by Kuro Games or the KuroBBS team. Game names, icons and data-endpoint rights belong to their respective owners. This project exists for local data display and gameplay convenience only.

## Special Thanks

Some features and implementations reference these open-source projects — many thanks:

- [Haiyu](https://github.com/HaiyuGame/Haiyu) — gacha analysis algorithm (pity / miss rate / luck score), banner UP schedules, launcher interactions (process monitoring / minimize placement / post-game window state), update-channel fallback design
- [WutheringWavesTool](https://github.com/leck995/WutheringWavesTool) — log decryption, gacha endpoints, local data parsing, Tower of Adversity / Mortal Matrix / Wastes parsing
- [Sparkle.Live2DView](https://github.com/Mozi216/Sparkle.Live2DView) — Live2D rendering control for the Home page (Avalonia OpenGL, MIT), built on [Live2DCSharpSDK](https://github.com/Coloryr/Live2DCSharpSDK) (C# Cubism SDK, MIT)
