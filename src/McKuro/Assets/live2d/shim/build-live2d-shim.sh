#!/bin/bash
# ============================================================================
# build-live2d-shim.sh — 生成 Cubism Core 兼容层(libLive2DCubismCore.dylib / .so)
#
# 背景:McKuro 的 C# 绑定 (Live2DCSharpSDK.Framework) 是针对 Cubism 4 写的,
#       P/Invoke EntryPoint 写死 csmGetDrawableRenderOrders;而随包的「心」模型是
#       moc3 version 5,只有 Cubism 5 的 Core 能读。Cubism 5 把该 API 改名成了
#       csmGetRenderOrders,于是需要在两者之间加一层符号转发。
#       详见 shim.c 顶部注释与 Live2DLocator.cs 的说明。
#
# 用法:
#   build-live2d-shim.sh <Cubism SDK 解压目录> <输出目录>
#   例:build-live2d-shim.sh ~/Downloads/CubismSdkForNative-5-r.5 src/McKuro/Assets/live2d
#
# 需要:macOS 上要 clang + lipo;Linux 上要 gcc/cc。
# 产物:libLive2DCubismCore.dylib(macOS universal)/ libLive2DCubismCore.so(Linux x86_64)
# ============================================================================
set -euo pipefail

SDK_DIR="${1:?用法: build-live2d-shim.sh <Cubism SDK 目录> <输出目录>}"
OUT_DIR="${2:?用法: build-live2d-shim.sh <Cubism SDK 目录> <输出目录>}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SHIM_C="$SCRIPT_DIR/shim.c"

[ -f "$SHIM_C" ] || { echo "build-shim: 缺少 shim.c ($SHIM_C)" >&2; exit 1; }
[ -d "$SDK_DIR/Core" ] || { echo "build-shim: $SDK_DIR 不像 Cubism SDK(缺 Core/)" >&2; exit 1; }
mkdir -p "$OUT_DIR"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# 静态库随平台/架构不同;同时兼容 R5 的目录布局
find_core_lib() {
  local pattern="$1"
  local hit
  hit="$(find "$SDK_DIR/Core/lib" -path "$pattern" -name 'libLive2DCubismCore.a' 2>/dev/null | head -1)"
  [ -n "$hit" ] || { echo "build-shim: 找不到静态库 $pattern" >&2; exit 1; }
  echo "$hit"
}

if [ "$(uname -s)" = "Darwin" ]; then
  echo "build-shim: macOS,生成 universal dylib"
  ARM_LIB="$(find_core_lib '*macos/arm64/*')"
  X64_LIB="$(find_core_lib '*macos/x86_64/*')"

  # 静态库的构建目标可能比本机 SDK 新,放宽版本下限避免 ld 报错;不影响运行
  for arch in arm64 x86_64; do
    lib="$ARM_LIB"; [ "$arch" = "x86_64" ] && lib="$X64_LIB"
    clang -dynamiclib -arch "$arch" -mmacosx-version-min=12.0 -O2 -fvisibility=hidden \
      "$SHIM_C" "$lib" -o "$WORK/core-$arch.dylib" 2>&1 | grep -v "warning: object file" || true
    [ -f "$WORK/core-$arch.dylib" ] || { echo "build-shim: $arch 编译失败" >&2; exit 1; }
    install_name_tool -id "@rpath/libLive2DCubismCore.dylib" "$WORK/core-$arch.dylib"
  done

  lipo -create "$WORK/core-arm64.dylib" "$WORK/core-x86_64.dylib" \
    -output "$OUT_DIR/libLive2DCubismCore.dylib"
  echo "build-shim: 完成 → $OUT_DIR/libLive2DCubismCore.dylib"
  nm -gU "$OUT_DIR/libLive2DCubismCore.dylib" | grep -E "csmGetDrawableRenderOrders|csmGetRenderOrders" || true
else
  echo "build-shim: 非 macOS,生成 .so"
  LIB="$(find_core_lib '*linux*')"
  cc -shared -fPIC -O2 -fvisibility=hidden "$SHIM_C" "$LIB" -o "$OUT_DIR/libLive2DCubismCore.so"
  echo "build-shim: 完成 → $OUT_DIR/libLive2DCubismCore.so"
  nm -D "$OUT_DIR/libLive2DCubismCore.so" | grep -E "csmGetDrawableRenderOrders|csmGetRenderOrders" || true
fi

# ---------------------------------------------------------------------------
# 附:在 macOS 上交叉编译 Linux .so(本仓库的实际做法)
#
#   brew install zig
#   zig cc -target x86_64-linux-gnu -shared -fPIC -O2 -fvisibility=hidden \
#     shim.c <SDK>/Core/lib/linux/x86_64/libLive2DCubismCore.a \
#     -o libLive2DCubismCore.so
#
# 为什么用 zig:macOS 自带 clang 没有 Linux 的 C 头文件,直接加 --target 会报
#   'stdlib.h' file not found。zig 自带完整 libc 头文件与 lld,可跨平台产出
#   合法的 ELF 共享库(已实测)。产物用 nm -D 校验两个符号都在即可。
# ---------------------------------------------------------------------------
