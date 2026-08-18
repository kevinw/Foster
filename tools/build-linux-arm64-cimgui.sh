#!/usr/bin/env bash
#
# Build cimgui (the C API wrapper Dear ImGui, used by ImGui.NET) for Linux
# arm64 (native, no emulation) and stage it into
# Foster/Framework/Internal/Native/linux-arm64/libcimgui.so.
#
# The ImGui.NET NuGet package (github.com/ImGuiNET/ImGui.NET) gets its native
# binaries from a separate release repo, github.com/ImGuiNET/ImGui.NET-nativebuild,
# which publishes cimgui.{so,dylib,dll} for osx/win-x86/win-x64/win-arm64/linux-x64
# -- but never linux-arm64. So on arm64 Linux the app tries to dlopen an
# x86_64 .so (or finds nothing) and fails with DllNotFoundException.
#
# Pinned to the exact cimgui commit used by ImGui.NET-nativebuild's "v1.91.6"
# release tag, which matches the "1.91.6.1" ImGui.NET C# package version
# referenced by Myth.csproj/Sokoban.csproj (the ".1" is a C#-only bindings
# patch bump with no corresponding native rebuild -- v1.91.6 is the native
# build that goes with it). Building any other cimgui commit risks a C-API
# mismatch against the generated C# bindings.
#
# Usage:  Foster/tools/build-linux-arm64-cimgui.sh
#
set -euo pipefail

CIMGUI_COMMIT="970c614802935f51f451aa21ae06e838bdcf9349"

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
out_dir="$script_dir/../Framework/Internal/Native/linux-arm64"
image="ubuntu:24.04"

mkdir -p "$out_dir"

echo ">> Building cimgui (linux-arm64, native) into $out_dir"

podman run --rm \
  --memory 8g --cpus 10 \
  -e CIMGUI_COMMIT="$CIMGUI_COMMIT" \
  -v "$out_dir":/out:Z \
  "$image" bash -euxc '
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y --no-install-recommends ca-certificates git cmake make build-essential

    cd /tmp
    git clone https://github.com/cimgui/cimgui.git
    cd cimgui
    git checkout "$CIMGUI_COMMIT"
    git submodule update --init --recursive

    mkdir -p build/Release
    cd build/Release
    cmake ../.. -DCMAKE_BUILD_TYPE=Release
    make -j"$(nproc)"

    # cimgui'"'"'s CMakeLists sets PREFIX "" so the raw build output is
    # "cimgui.so"; ImGui.NET'"'"'s own packaging renames it to "libcimgui.so"
    # (matches the linux-x64 asset already in the NuGet cache), which is
    # also what .NET DllImport("cimgui") resolves to first on Linux.
    test -s cimgui.so
    cp cimgui.so /out/libcimgui.so
    chmod 644 /out/libcimgui.so
    ls -l /out
  '

if [[ ! -s "$out_dir/libcimgui.so" ]]; then
  echo "!! Build failed: $out_dir/libcimgui.so missing" >&2
  exit 1
fi

echo ">> Done. Staged:"
ls -l "$out_dir"
file "$out_dir/libcimgui.so"
