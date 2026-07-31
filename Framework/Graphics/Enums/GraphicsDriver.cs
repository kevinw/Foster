namespace Foster.Framework;

/// <summary>
/// Available backing Graphic Drivers
/// </summary>
public enum GraphicsDriver
{
	None,
	Private,
	Vulkan,
	D3D12,
	Metal,
	WebGPU,
	Headless
}

/// <summary>
/// <see cref="GraphicsDriver"/> Extension Methods
/// </summary>
public static class GraphicsDriverExt
{
	/// <summary>
	/// Gets the standard extension name for the shaders of the given driver type.<br/>
	/// (For example, Vulkan is spv, Metal is msl, D3D12 is dxil, etc)
	/// </summary>
	public static string GetShaderExtension(this GraphicsDriver driver) => driver switch
	{
		GraphicsDriver.None => string.Empty,
		GraphicsDriver.Private => "spv",
		GraphicsDriver.Vulkan => "spv",
		GraphicsDriver.D3D12 => "dxil",
		GraphicsDriver.Metal => "msl",
		GraphicsDriver.WebGPU => "wgsl",
		GraphicsDriver.Headless => "spv",
		_ => throw new NotImplementedException(),
	};
}
