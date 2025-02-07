# Vulkan Renderer Demo

A 3D renderer written in C# using Silk.NET with Vulkan. This renderer implements the following techniques:
- Deferred shading
- Directional and omnidirectional lights
- Real-time directional and omnidirectional shadow maps
- PBR lighting and materials
- Image based lighting using a skybox
- Normal mapping
- HDR tonemapping and gamma correction
- Bloom

## How to Build
1. Install .NET 8.0.
2. Install the Vulkan SDK for Windows or the Vulkan packages on Linux (as required by the [Khronos Vulkan Tutorial](https://docs.vulkan.org/tutorial/latest/02_Development_environment.html)).
3. (Optional) Install the glslc shader compiler as used in the Khronos Vulkan Tutorial and add the executable to your path. This will be used if you wish to recompile the SPIR-V shader byte code files from the source glsl files.
4. Use the `dotnet run` command in the terminal to build and run the project.

## Future Work
- Screen Space Ambient Occlusion
- Occlusion for objects and lights to improve performance
- Physically based bloom
- Batching draw calls to reduce overhead
- Improved support for different 3D models and materials

## References
- [Learn OpenGL](https://learnopengl.com)
- [Khronos Vulkan Tutorial](https://docs.vulkan.org/tutorial/latest/00_Introduction.html)
- [Vulkan C++ examples and demos](https://github.com/SaschaWillems/Vulkan)
- [ACES Filmic Tonemapping Curve](https://knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/)
