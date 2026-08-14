# macOS shadercross

This directory is a relocatable shader compiler bundle. `shadercross` loads its
dependencies through `@loader_path`; SDL3 reuses Foster's runtime copy.

`libdxcompiler.dylib` and `libspirv-cross-c-shared.0.dylib` come from the
LunarG Vulkan SDK 1.4.304.0 archive (`d8909362c8cd6a61a33f8ab21240c6d3f38c3818f443f52939a9ac198ad0e539`).
