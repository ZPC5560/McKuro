#!/bin/bash
# ============================================================================
# make-linux-packages.sh — 把 AOT publish 产物打包为 .deb 与 .rpm
# 用法: make-linux-packages.sh <publish目录> <版本号> <输出目录>
# 需在 Linux 上运行(.deb 依赖 dpkg-deb;.rpm 依赖 rpmbuild,Ubuntu 上 apt install rpm)。
# 布局:/opt/McKuro/(程序目录,含 McKuro 二进制与全部资源)+ freedesktop 桌面项/图标。
# libmpv 为 Recommends(缺失时应用自动回退静态封面)。
# ============================================================================
set -euo pipefail

PUB="$(cd "$1" && pwd)"
VER="$2"
OUT="$3"
case "$OUT" in
  /*) ;;
  *) OUT="$PWD/$OUT" ;;
esac
mkdir -p "$OUT"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ICON_SRC="$SCRIPT_DIR/../src/McKuro/Assets/shorekeeper_icon.png"
[ -f "$ICON_SRC" ] || { echo "make-pkg: 图标源缺失 $ICON_SRC" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
PKG="$WORK/root"

# ---- 公共目录树 ----
mkdir -p "$PKG/opt/McKuro" "$PKG/usr/share/applications" \
         "$PKG/usr/share/icons/hicolor/256x256/apps" "$PKG/usr/share/pixmaps"
cp -R "$PUB"/. "$PKG/opt/McKuro/"
find "$PKG/opt/McKuro" -name '*.pdb' -delete
rm -rf "$PKG/opt/McKuro/McKuro.dSYM"
# hpatchz.exe 为 Windows 差分工具,Linux 包剔除
rm -rf "$PKG/opt/McKuro/Assets/HpatchzResource"
chmod +x "$PKG/opt/McKuro/McKuro"
cp "$ICON_SRC" "$PKG/usr/share/icons/hicolor/256x256/apps/mckuro.png"
cp "$ICON_SRC" "$PKG/usr/share/pixmaps/mckuro.png"

cat > "$PKG/usr/share/applications/McKuro.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=McKuro
Name[zh_CN]=鸣潮启动器
GenericName=Game Launcher
Comment=Wuthering Waves desktop launcher
Comment[zh_CN]=《鸣潮》桌面启动器
Exec=/opt/McKuro/McKuro
Icon=mckuro
Terminal=false
Categories=Game;Utility;
StartupWMClass=McKuro
EOF

# ---- .deb ----
mkdir -p "$PKG/DEBIAN"
cat > "$PKG/DEBIAN/control" <<EOF
Package: mckuro
Version: $VER
Section: games
Priority: optional
Architecture: amd64
Maintainer: ZPC5560 <zpc5560@users.noreply.github.com>
Homepage: https://github.com/ZPC5560/McKuro
Recommends: libmpv2
Description: Wuthering Waves desktop launcher (McKuro)
 Avalonia-based desktop launcher for Wuthering Waves: game update and
 repair, gacha analysis, daily sign-in, character data, activities,
 redemption codes and playtime statistics.
EOF
dpkg-deb --root-owner-group -Zxz -b "$PKG" "$OUT/mckuro_${VER}_amd64.deb"
echo "make-pkg: deb 完成"

# ---- .rpm(有 rpmbuild 才构建;预置树直装,不做 %build) ----
if command -v rpmbuild >/dev/null 2>&1; then
  TOP="$WORK/rpmbuild"
  mkdir -p "$TOP"/{BUILD,RPMS,SOURCES,SPECS}
  cp -a "$PKG/opt" "$PKG/usr" "$TOP/BUILD/"
  cat > "$TOP/SPECS/mckuro.spec" <<EOF
Name:     mckuro
Version:  $VER
Release:  1
Summary:  Wuthering Waves desktop launcher
License:  Proprietary
URL:      https://github.com/ZPC5560/McKuro
BuildArch: x86_64

%description
Avalonia-based desktop launcher for Wuthering Waves.

%prep
true

%build
true

%install
cp -a "$TOP/BUILD/opt" "%{buildroot}/"
cp -a "$TOP/BUILD/usr" "%{buildroot}/"

%files
/opt/McKuro
/usr/share/applications/McKuro.desktop
/usr/share/icons/hicolor/256x256/apps/mckuro.png
/usr/share/pixmaps/mckuro.png

%post
update-desktop-database >/dev/null 2>&1 || true
EOF
  rpmbuild -bb --target x86_64 --define "_topdir $TOP" "$TOP/SPECS/mckuro.spec" >/dev/null
  cp "$TOP/RPMS/x86_64/mckuro-$VER-1.x86_64.rpm" "$OUT/"
  echo "make-pkg: rpm 完成"
else
  echo "make-pkg: 警告 无 rpmbuild,跳过 rpm" >&2
fi

ls -la "$OUT"
