### Cross-Compiling Shaders
Shaders are generated using [SDL's shadercross](https://github.com/libsdl-org/SDL_shadercross) tool to generate SPIR-V/DXIL/MSL shaders (and via `dxc` + `naga` for WebGPU/WGSL). Compilation is driven by the MSBuild target in `msbuild/ShaderCompilation.targets`, which runs automatically on build. To compile manually:

```
dotnet build -t:CompileShaders [/p:ShaderPlatform=mac|windows|linux|web]
```
