#!/bin/bash
# ============================================================================
# bundle-mpv-macos.sh — 把 libmpv 及其完整依赖树打包进应用输出目录的 libmpv/
# 子目录,并全部改写为 @loader_path 相对加载路径。应用自带播放环境,
# 目标机器无需安装 Homebrew / mpv 即可播放启动页背景视频。
#
# 用法: bundle-mpv-macos.sh <输出目录> [libmpv源dylib路径]
#   源路径省略时按顺序探测: 脚本旁 libmpv-source/ (可检入仓库做离线构建)
#   → /opt/homebrew/lib/libmpv.2.dylib (Apple Silicon Homebrew)
#   → /usr/local/lib/libmpv.2.dylib (Intel Homebrew)
#
# 原理:
#   1) 从源 dylib 出发,用 otool -L 递归收集全部依赖;跳过系统库
#      (/usr/lib, /System/Library, /Library — 所有 macOS 自带)。
#   2) 用 cp -L 解引用符号链接复制到 <输出目录>/libmpv/。
#   3) 逐 dylib 改写:Homebrew 绝对路径(/opt/homebrew、/usr/local)的
#      LC_LOAD_DYLIB / LC_ID_DYLIB 字符串直接做二进制级替换为
#      @loader_path/<同名>(短路径,余下补 NUL)。不用 install_name_tool:
#      新版 Xcode 链接器产物(LC_DYLD_CHAINED_FIXUPS)会让它报
#      "link edit information does not fill the __LINKEDIT segment"。
#   4) 改写前 codesign 去签,改写后 ad-hoc 重签(arm64 必须带签名)。
#   5) 校验:打包后的库不得再引用任何 Homebrew 绝对路径。
#
# 产物布局: <输出目录>/libmpv/libmpv.2.dylib + 全部依赖 dylib
# 应用侧:   MpvApi.RootPath 指向该目录(MacFunctionResolver 的最后搜索路径),
#           软件渲染默认走 libmpv 的软件解码,不触碰 Vulkan/Metal。
#
# 说明: libmpv/ffmpeg 等为 LGPL 动态库,随应用分发时需保留 LGPL 合规
#       (提供源码获取方式、允许替换链接),详见各库 LICENSE。
# ============================================================================
set -euo pipefail

OUT_DIR="${1:?用法: bundle-mpv-macos.sh <输出目录> [libmpv源dylib路径]}"
SRC="${2:-}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEST="$OUT_DIR/libmpv"

# ---- 定位源 libmpv ----
if [ -z "$SRC" ]; then
  for p in \
    "$SCRIPT_DIR/libmpv-source/libmpv.2.dylib" \
    /opt/homebrew/lib/libmpv.2.dylib \
    /usr/local/lib/libmpv.2.dylib; do
    if [ -f "$p" ]; then
      SRC="$p"
      break
    fi
  done
fi

if [ -z "$SRC" ] || [ ! -f "$SRC" ]; then
  echo "bundle-mpv: 未找到 libmpv.2.dylib。请先执行: brew install mpv" >&2
  echo "bundle-mpv: 或把 dylib 放到 $SCRIPT_DIR/libmpv-source/ 做离线构建" >&2
  exit 0   # 不阻断构建:应用会优雅回退静态封面图
fi

echo "bundle-mpv: 源库 $SRC"

# ---- 第一步:递归收集并复制依赖 ----
rm -rf "$DEST"
mkdir -p "$DEST"

SEEN_FILE="$(mktemp)"
trap 'rm -f "$SEEN_FILE"' EXIT

copy_dylib() {
  local src="$1"
  [ -f "$src" ] || return 0

  # 去重(按加载路径字符串;同名不同路径的库 basename 相同,复制结果一致)
  if grep -qxF "$src" "$SEEN_FILE"; then
    return 0
  fi
  echo "$src" >> "$SEEN_FILE"

  local name
  name="$(basename "$src")"

  if [ -f "$DEST/$name" ]; then
    echo "bundle-mpv: 警告 同名库冲突,跳过: $src" >&2
    return 0
  fi

  cp -Lf "$src" "$DEST/$name"
  chmod u+w "$DEST/$name"   # Homebrew bottle 是只读的,后续要改写
  echo "bundle-mpv:   打包 $name"

  # 先读入数组再遍历:递归调用会复用 SEEN_FILE,不能边读边递归
  local dep
  local -a deps
  deps=()
  while read -r dep; do
    deps+=("$dep")
  done < <(otool -L "$src" | tail -n +2 | awk '{print $1}')

  for dep in "${deps[@]}"; do
    case "$dep" in
      /usr/lib/*|/System/*|/Library/*)
        ;; # 系统库,不打包
      @*)
        echo "bundle-mpv: 警告 发现相对依赖,原样保留: $src -> $dep" >&2
        ;;
      *)
        copy_dylib "$dep" ;;
    esac
  done
}

copy_dylib "$SRC"

COUNT="$(find "$DEST" -name '*.dylib' | wc -l | tr -d ' ')"
echo "bundle-mpv: 已打包 $COUNT 个 dylib ($(du -sh "$DEST" | awk '{print $1}'))"

# ---- 第二步:改写 Homebrew 绝对路径为 @loader_path 并重签 ----
echo "bundle-mpv: 改写加载路径为 @loader_path ..."

# perl 二进制替换:把 Mach-O 头里 LC_LOAD_DYLIB/LC_ID_DYLIB 的
# /opt/homebrew、/usr/local 绝对路径替换为 @loader_path/<basename>(补 NUL)。
# 注意:必须用单引号 heredoc,避免 bash 展开 perl 的 $ 变量。
read -r -d '' PATCHER <<'PERLEOF' || true
use bytes;
my ($file, @deps) = @ARGV;
open my $fh, "<:raw", $file or die "open $file: $!";
local $/; my $data = <$fh>; close $fh;
my $changed = 0;
for my $dep (@deps) {
    # 注意: @loader_path 必须用单引号,perl 双引号会把 @loader_path 当数组插值成空串
    my $new = '@loader_path/' . (split m{/}, $dep)[-1];
    die "new longer than old: $dep\n" if length($new) > length($dep);
    my $padded = $new . "\0" x (length($dep) - length($new));
    my $count = ($data =~ s/\Q$dep\E/$padded/g);
    $changed += $count;
    warn "bundle-mpv: 警告 路径未找到: $dep (in $file)\n" unless $count;
}
open my $out, ">:raw", $file or die "write $file: $!";
print $out $data; close $out;
print "bundle-mpv:   改写 $file ($changed 处)\n";
PERLEOF

for dylib in "$DEST"/*.dylib; do
  [ -f "$dylib" ] || continue

  # 收集该库引用的全部 Homebrew 绝对路径(含第一行 LC_ID_DYLIB)
  refs=()
  while read -r ref; do
    case "$ref" in
      /opt/homebrew/*|/usr/local/*) refs+=("$ref") ;;
    esac
  done < <(otool -L "$dylib" | awk '{print $1}')

  [ "${#refs[@]}" -eq 0 ] && continue

  codesign --remove-signature "$dylib" 2>/dev/null || true
  perl -e "$PATCHER" "$dylib" "${refs[@]}"
  codesign --force --sign - "$dylib" 2>/dev/null
done

# ---- 第三步:校验无 Homebrew 绝对路径残留 ----
LEFTOVER=0
for dylib in "$DEST"/*.dylib; do
  [ -f "$dylib" ] || continue
  if otool -L "$dylib" | grep -qE '(/opt/homebrew|/usr/local)'; then
    echo "bundle-mpv: 错误 $dylib 仍引用 Homebrew 路径:" >&2
    otool -L "$dylib" | grep -E '(/opt/homebrew|/usr/local)' >&2
    LEFTOVER=1
  fi
done

if [ "$LEFTOVER" -ne 0 ]; then
  echo "bundle-mpv: 打包失败,存在未改写的依赖" >&2
  exit 1
fi

echo "bundle-mpv: 完成 → $DEST"
