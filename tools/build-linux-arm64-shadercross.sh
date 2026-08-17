#!/usr/bin/env bash
#
# Build the SDL_shadercross CLI for Linux arm64 and stage it (plus its runtime
# .so dependencies) into Foster/tools/linux-arm64/, for native shader
# compilation on arm64 Linux (e.g. Asahi Linux on Apple Silicon) with no
# x86_64 emulation involved.
#
# Unlike build-linux-shadercross.sh (which downloads a prebuilt x86_64 DXC
# binary from upstream), there is no prebuilt Linux arm64 DXC available
# anywhere (checked both upstream DirectXShaderCompiler releases and the
# LunarG Vulkan SDK, which is x86_64-only on Linux). So this script builds
# DXC from source via SDL_shadercross's "vendored" CMake path
# (SDLSHADERCROSS_VENDORED=ON). This is not exotic: SDL_shadercross's own CI
# already exercises this exact path for its "Steam Linux Runtime (Sniper)"
# and macOS jobs. It works on any architecture because DXC's
# PredefinedParams.cmake sets LLVM_TARGETS_TO_BUILD=None -- no x86/ARM
# machine-code backends are compiled. DXIL output is just serialized LLVM
# bitcode, and SPIR-V output comes from DXC's own custom emitter, not an
# LLVM target backend. So nothing architecture-specific is ever built; it's
# just LLVM/Clang's core infra + the HLSL frontend, compiled with whatever
# host compiler you point at it.
#
# Runs inside a native (no emulation, no --arch flag) podman container on
# arm64 Linux, so it's just as fast as any other native build.
#
# Usage:  Foster/tools/build-linux-arm64-shadercross.sh
#
set -euo pipefail

# Pinned versions -- kept in sync with build-linux-shadercross.sh so the
# x86_64 and arm64 Linux binaries come from the same upstream commits.
SDL_TAG="release-3.4.10"
SHADERCROSS_REF="9a4616443083"   # SDL_shadercross main @ 2026-06

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
out_dir="$script_dir/linux-arm64"
image="ubuntu:24.04"

mkdir -p "$out_dir"

echo ">> Building Linux shadercross (arm64, native) into $out_dir"

podman run --rm \
  --memory 32g --cpus 10 \
  -e SDL_TAG="$SDL_TAG" \
  -e SHADERCROSS_REF="$SHADERCROSS_REF" \
  -v "$out_dir":/out:Z \
  "$image" bash -euxc '
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y --no-install-recommends \
      build-essential cmake ninja-build git python3 patchelf pkg-config ca-certificates

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

    # --- SDL_shadercross, vendored: builds SPIRV-Cross + DXC from source --
    git clone https://github.com/libsdl-org/SDL_shadercross.git
    cd SDL_shadercross
    git checkout "$SHADERCROSS_REF"
    git submodule update --init --recursive

    cmake -S . -B build -GNinja \
      -DCMAKE_BUILD_TYPE=Release \
      -DCMAKE_PREFIX_PATH=/tmp/prefix \
      -DSDLSHADERCROSS_VENDORED=ON \
      -DSDLSHADERCROSS_SPIRVCROSS_SHARED=ON \
      -DSDLSHADERCROSS_CLI=ON \
      -DSDLSHADERCROSS_DXC=ON \
      -DSDLSHADERCROSS_SHARED=OFF -DSDLSHADERCROSS_STATIC=ON \
      -DSDLSHADERCROSS_TESTS=OFF -DSDLSHADERCROSS_INSTALL=OFF
    cmake --build build --parallel "$(nproc)"

    # --- Stage binary + runtime libs into one flat dir -------------------
    stage=/tmp/stage
    mkdir -p "$stage"
    cp build/shadercross "$stage/"

    # Copy exactly the non-system libraries the binary needs, resolving each
    # SONAME symlink to its real file and keeping the SONAME as the filename.
    for so in $(patchelf --print-needed build/shadercross); do
      src=$(find /tmp/prefix build -name "$so" -print -quit 2>/dev/null || true)
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
