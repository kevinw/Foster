#!/usr/bin/env bash
#
# Build the SDL_shadercross CLI for Linux x86_64 and stage it (plus its runtime
# .so dependencies) into Foster/tools/linux/, so headless asset-pack builds on
# GitHub Actions (ubuntu-latest, x86_64) can compile shaders the same way macOS
# and Windows already do.
#
# This runs inside an Apple `container` (https://github.com/apple/container)
# forced to --arch amd64. On Apple Silicon that is emulated, but the build is
# fast because DXC is downloaded prebuilt (no LLVM compile). The produced binary
# is x86_64-only and is intended for CI; it will NOT run on arm64 Linux.
#
# Mirrors upstream's non-vendored CI recipe:
#   libsdl-org/SDL_shadercross/.github/workflows/main.yml  ("Ubuntu 24.04" row)
#
# Usage:  Foster/tools/build-linux-shadercross.sh
#
set -euo pipefail

# Pinned versions (bump these to update the committed binary).
SDL_TAG="release-3.4.16"
SHADERCROSS_REF="9a4616443083"   # SDL_shadercross main @ 2026-06

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
out_dir="$script_dir/linux"
image="ubuntu:24.04"

mkdir -p "$out_dir"

echo ">> Building Linux shadercross (amd64) into $out_dir"

container image pull --platform linux/amd64 "$image" >/dev/null 2>&1 || true

container run --rm --arch amd64 \
  --memory 8g --cpus 6 \
  -e SDL_TAG="$SDL_TAG" \
  -e SHADERCROSS_REF="$SHADERCROSS_REF" \
  -v "$out_dir":/out \
  "$image" bash -euxc '
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y --no-install-recommends \
      build-essential cmake ninja-build git wget ca-certificates patchelf pkg-config

    cd /tmp

    # --- SDL3 (headless: shadercross does offline compilation, no video) --
    git clone --depth 1 --branch "$SDL_TAG" https://github.com/libsdl-org/SDL.git
    cmake -S SDL -B sdl-build -GNinja \
      -DCMAKE_BUILD_TYPE=Release \
      -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST_LIBRARY=OFF \
      -DSDL_UNIX_CONSOLE_BUILD=ON \
      -DSDL_X11=OFF -DSDL_WAYLAND=OFF -DSDL_VULKAN=OFF -DSDL_OPENGL=OFF \
      -DSDL_OPENGLES=OFF -DSDL_RENDER=OFF -DSDL_AUDIO=OFF -DSDL_CAMERA=OFF \
      -DCMAKE_INSTALL_PREFIX=/tmp/prefix
    cmake --build sdl-build
    cmake --install sdl-build

    # --- SDL_shadercross (+ SPIRV-Cross submodule) -----------------------
    git clone https://github.com/libsdl-org/SDL_shadercross.git
    cd SDL_shadercross
    git checkout "$SHADERCROSS_REF"
    git submodule update --init external/SPIRV-Cross

    # Prebuilt DXC (x86_64 libdxcompiler.so) — no LLVM compile.
    cmake -P build-scripts/download-prebuilt-DirectXShaderCompiler.cmake
    export DirectXShaderCompiler_ROOT="$PWD/external/DirectXShaderCompiler-binaries"

    # SPIRV-Cross (shared) into the same prefix.
    cmake -S external/SPIRV-Cross -B spirv-build -GNinja \
      -DCMAKE_BUILD_TYPE=Release \
      -DSPIRV_CROSS_SHARED=ON -DSPIRV_CROSS_STATIC=ON \
      -DSPIRV_CROSS_CLI=OFF -DSPIRV_CROSS_ENABLE_TESTS=OFF \
      -DCMAKE_INSTALL_PREFIX=/tmp/prefix
    cmake --build spirv-build --parallel 2
    cmake --install spirv-build

    # shadercross CLI (non-vendored: uses the SDL3/SPIRV-Cross/DXC above).
    cmake -S . -B build -GNinja \
      -DCMAKE_BUILD_TYPE=Release \
      -DCMAKE_PREFIX_PATH=/tmp/prefix \
      -DSDLSHADERCROSS_VENDORED=OFF \
      -DSDLSHADERCROSS_CLI=ON \
      -DSDLSHADERCROSS_DXC=ON \
      -DSDLSHADERCROSS_SHARED=OFF -DSDLSHADERCROSS_STATIC=ON \
      -DSDLSHADERCROSS_TESTS=OFF -DSDLSHADERCROSS_INSTALL=OFF
    cmake --build build --parallel 2

    # --- Stage binary + runtime libs into one flat dir -------------------
    stage=/tmp/stage
    mkdir -p "$stage"
    cp build/shadercross "$stage/"

    # Copy exactly the non-system libraries the binary needs, resolving each
    # SONAME symlink to its real file and keeping the SONAME as the filename.
    for so in $(patchelf --print-needed build/shadercross); do
      src=""
      for dir in /tmp/prefix/lib "$DirectXShaderCompiler_ROOT/linux/lib"; do
        if [ -e "$dir/$so" ]; then src="$dir/$so"; break; fi
      done
      [ -n "$src" ] && cp -L "$src" "$stage/$so"   # system libs (libc, libstdc++, ...) skipped
    done

    # Make the binary find its sibling .so files with no LD_LIBRARY_PATH.
    patchelf --set-rpath "\$ORIGIN" "$stage/shadercross"

    # Smoke test: --help and a trivial HLSL->SPIRV compile.
    cd "$stage"
    ./shadercross --help >/dev/null
    cat > /tmp/t.hlsl <<EOF
struct VSOut { float4 pos : SV_Position; };
VSOut vertex_main(uint id : SV_VertexID) {
  VSOut o; o.pos = float4(0,0,0,1); return o;
}
EOF
    ./shadercross /tmp/t.hlsl -s HLSL -t vertex -e vertex_main -o /tmp/t.spv
    test -s /tmp/t.spv

    rm -f /out/* 2>/dev/null || true
    cp "$stage"/* /out/   # plain cp: avoid -a setting times on the bind-mount root
    chmod +x /out/shadercross
    ls -l /out
  '

if [[ ! -s "$out_dir/shadercross" ]]; then
  echo "!! Build failed: $out_dir/shadercross missing" >&2
  exit 1
fi

echo ">> Done. Staged:"
ls -l "$out_dir"
file "$out_dir/shadercross"
