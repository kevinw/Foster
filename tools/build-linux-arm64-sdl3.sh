#!/usr/bin/env bash
#
# Build SDL3 for Linux arm64 (native, no emulation) and stage it into
# Foster/Framework/Internal/Native/linux-arm64/libSDL3.so, for running Foster
# games on arm64 Linux (e.g. Asahi Linux on Apple Silicon).
#
# Unlike the trimmed headless SDL3 build used by build-linux-arm64-shadercross.sh
# (which only needs offline shader compilation, no windowing), this needs real
# video (Wayland/X11), SDL_GPU with Vulkan, and controller support.
#
# Foster does not use SDL's audio, renderer, camera, general haptics, power,
# sensor, or tray APIs, so those subsystems are omitted.
#
# Usage:  Foster/tools/build-linux-arm64-sdl3.sh
#
set -euo pipefail

SDL_TAG="release-3.4.16"

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
out_dir="$script_dir/../Framework/Internal/Native/linux-arm64"
image="ubuntu:24.04"

mkdir -p "$out_dir"

echo ">> Building SDL3 (linux-arm64, native) into $out_dir"

podman run --rm \
  --memory 16g --cpus 10 \
  -e SDL_TAG="$SDL_TAG" \
  -v "$out_dir":/out:Z \
  "$image" bash -euxc '
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y --no-install-recommends \
      ca-certificates git cmake make ninja-build pkg-config build-essential \
      libfribidi-dev libusb-1.0-0-dev libx11-dev libxext-dev libxrandr-dev \
      libxcursor-dev libxfixes-dev libxi-dev libxss-dev libxtst-dev \
      libwayland-dev libxkbcommon-dev libdbus-1-dev libibus-1.0-dev \
      libudev-dev libthai-dev libdecor-0-dev

    cd /tmp
    git clone --depth 1 --branch "$SDL_TAG" https://github.com/libsdl-org/SDL.git
    cmake -S SDL -B build -GNinja \
      -DCMAKE_BUILD_TYPE=Release \
      -DSDL_SHARED=ON -DSDL_STATIC=OFF \
      -DSDL_TEST_LIBRARY=OFF \
      -DSDL_AUDIO=OFF -DSDL_RENDER=OFF -DSDL_CAMERA=OFF \
      -DSDL_HAPTIC=OFF -DSDL_POWER=OFF -DSDL_SENSOR=OFF -DSDL_TRAY=OFF \
      -DSDL_OPENGL=OFF -DSDL_OPENGLES=OFF -DSDL_DUMMYVIDEO=OFF -DSDL_OFFSCREEN=ON \
      -DSDL_VIRTUAL_JOYSTICK=OFF -DSDL_LIBURING=OFF \
      -DSDL_KMSDRM=OFF -DSDL_RPI=OFF -DSDL_ROCKCHIP=OFF \
      -DCMAKE_INSTALL_PREFIX=/tmp/prefix
    cmake --build build --parallel "$(nproc)"
    cmake --install build

    # Foster expects a flat "libSDL3.so" (not the usual libSDL3.so -> libSDL3.so.0
    # -> libSDL3.so.0.x.y symlink chain) -- matches the linux-x64 convention and
    # is what .NET DllImport("SDL3") resolves to via its standard search order.
    real_so=$(find /tmp/prefix/lib -maxdepth 1 -name "libSDL3.so.*" -type f | sort -V | tail -1)
    test -n "$real_so"
    cp -L "$real_so" /out/libSDL3.so
    strip --strip-unneeded /out/libSDL3.so
    chmod 644 /out/libSDL3.so
    ls -l /out
  '

if [[ ! -s "$out_dir/libSDL3.so" ]]; then
  echo "!! Build failed: $out_dir/libSDL3.so missing" >&2
  exit 1
fi

echo ">> Done. Staged:"
ls -l "$out_dir"
file "$out_dir/libSDL3.so"
