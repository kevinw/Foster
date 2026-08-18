#!/usr/bin/env bash
#
# Build SDL3 for Linux arm64 (native, no emulation) and stage it into
# Foster/Framework/Internal/Native/linux-arm64/libSDL3.so, for running Foster
# games on arm64 Linux (e.g. Asahi Linux on Apple Silicon).
#
# Foster/Framework/Internal/Native/linux-x64/libSDL3.so is x86_64-only, and
# Foster.Framework.csproj copies it unconditionally on Linux regardless of
# CPU arch, so on arm64 Linux the app tries to dlopen an x86_64 .so and
# fails with DllNotFoundException.
#
# Unlike the trimmed headless SDL3 build used by build-linux-arm64-shadercross.sh
# (which only needs offline shader compilation, no windowing), this is a full
# desktop build: Foster's Linux GraphicsDeviceSDL backend uses SDL3's GPU API
# with the Vulkan driver, so this needs real video (Wayland/X11), Vulkan
# (dlopen'd at runtime, not linked), and audio support.
#
# Package list below matches libsdl-org/setup-sdl's own apt-get list for
# building SDL3 (github.com/libsdl-org/setup-sdl, src/version.ts), which is
# what SDL's own CI uses -- so this should reproduce the same feature set as
# the existing linux-x64 build (confirmed via `strings` on that .so: Wayland,
# X11, Vulkan, PulseAudio, PipeWire, ALSA, libdecor are all present).
#
# Usage:  Foster/tools/build-linux-arm64-sdl3.sh
#
set -euo pipefail

# Pinned to the same tag as build-linux-arm64-shadercross.sh for consistency.
SDL_TAG="release-3.4.10"

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
      libasound2-dev libpulse-dev libaudio-dev libfribidi-dev libjack-dev \
      libsndio-dev libusb-1.0-0-dev libx11-dev libxext-dev libxrandr-dev \
      libxcursor-dev libxfixes-dev libxi-dev libxss-dev libxtst-dev \
      libwayland-dev libxkbcommon-dev libdrm-dev libgbm-dev libgl1-mesa-dev \
      libgles2-mesa-dev libegl1-mesa-dev libdbus-1-dev libibus-1.0-dev \
      libudev-dev libthai-dev libpipewire-0.3-dev libdecor-0-dev

    cd /tmp
    git clone --depth 1 --branch "$SDL_TAG" https://github.com/libsdl-org/SDL.git
    cmake -S SDL -B build -GNinja \
      -DCMAKE_BUILD_TYPE=Release \
      -DSDL_SHARED=ON -DSDL_STATIC=OFF \
      -DCMAKE_INSTALL_PREFIX=/tmp/prefix
    cmake --build build --parallel "$(nproc)"
    cmake --install build

    # Foster expects a flat "libSDL3.so" (not the usual libSDL3.so -> libSDL3.so.0
    # -> libSDL3.so.0.x.y symlink chain) -- matches the linux-x64 convention and
    # is what .NET DllImport("SDL3") resolves to via its standard search order.
    real_so=$(find /tmp/prefix/lib -maxdepth 1 -name "libSDL3.so.*" -type f | sort -V | tail -1)
    test -n "$real_so"
    cp -L "$real_so" /out/libSDL3.so
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
