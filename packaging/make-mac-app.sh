#!/bin/bash
# ============================================================================
# make-mac-app.sh — 把 AOT publish 产物打包为 McKuro.app(带应用图标 + Info.plist + ad-hoc 签名)
# 用法: make-mac-app.sh <publish目录> <版本号> <输出zip路径>
# 需在 macOS 上运行(依赖 sips/iconutil/codesign)。
# 图标源:src/McKuro/Assets/shorekeeper_icon.png(256x256,向上补 512/1024)。
# 未做开发者证书签名/公证:目标机首次右键→打开(或 xattr -d com.apple.quarantine)。
# ============================================================================
set -euo pipefail

PUB="$(cd "$1" && pwd)"
VER="$2"
OUT="$3"
case "$OUT" in
  /*) ;;
  *) OUT="$PWD/$OUT" ;;
esac
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ICON_SRC="$SCRIPT_DIR/../src/McKuro/Assets/shorekeeper_icon.png"
[ -f "$ICON_SRC" ] || { echo "make-app: 图标源缺失 $ICON_SRC" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
APP="$WORK/McKuro.app"

# ---- 骨架:publish 产物整体进 Contents/MacOS(libmpv/ 与 Assets/ 相对 exe 位置不变) ----
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB"/. "$APP/Contents/MacOS/"
find "$APP/Contents/MacOS" -name '*.pdb' -delete
rm -rf "$APP/Contents/MacOS/McKuro.dSYM"
chmod +x "$APP/Contents/MacOS/McKuro"

# ---- Info.plist ----
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>McKuro</string>
    <key>CFBundleDisplayName</key><string>McKuro · 鸣潮启动器</string>
    <key>CFBundleExecutable</key><string>McKuro</string>
    <key>CFBundleIdentifier</key><string>com.mckuro.launcher</string>
    <key>CFBundleVersion</key><string>$VER</string>
    <key>CFBundleShortVersionString</key><string>$VER</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

# ---- icns:256 源图生成全尺寸 iconset(512/1024 由 sips 放大补齐) ----
ICONSET="$WORK/AppIcon.iconset"
mkdir -p "$ICONSET"
for s in 16 32 64 128 256; do
  sips -z "$s" "$s" "$ICON_SRC" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z "$d" "$d" "$ICON_SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
sips -z 512 512 "$ICON_SRC" --out "$ICONSET/icon_512x512.png" >/dev/null
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
echo "make-app: icns 完成"

# ---- ad-hoc 签名(arm64 必须带签名;deep 覆盖内嵌 dylib) ----
codesign --force --deep --sign - "$APP" 2>/dev/null || echo "make-app: 警告 codesign 失败,继续(未签名包仍可右键打开)"

# ---- zip 打包(-y 保留符号链接,unix 权限位随 zip 保留) ----
(cd "$WORK" && zip -qry "$OUT" McKuro.app)
echo "make-app: $OUT ($(du -sh "$OUT" | awk '{print $1}'))"

# ---- .dmg 拖拽安装盘(同前缀输出:xxx.app.zip → xxx.dmg;含 /Applications 快捷方式) ----
DMG="${OUT%.app.zip}.dmg"
if [ "$DMG" = "$OUT" ]; then DMG="${OUT%.zip}.dmg"; fi
DMG_ROOT="$WORK/dmg"
mkdir -p "$DMG_ROOT"
cp -R "$APP" "$DMG_ROOT/McKuro.app"
ln -s /Applications "$DMG_ROOT/Applications"
hdiutil create -volname "McKuro $VER" -srcfolder "$DMG_ROOT" -ov -format UDZO "$DMG" >/dev/null
echo "make-app: $DMG ($(du -sh "$DMG" | awk '{print $1}'))"
