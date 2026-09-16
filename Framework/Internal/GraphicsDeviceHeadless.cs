using System.Runtime.CompilerServices;

namespace Foster.Framework;

internal class GraphicsDeviceHeadless(App app) : GraphicsDevice(app)
{
	public override GraphicsDriver Driver => GraphicsDriver.Headless;
	public override bool Disposed => disposed;
	public override bool VSync { get; set; }
	public override void InsertDebugLabel(string text) { }

	private bool disposed;
	private long nextResourceId;

	internal override void CreateDevice(in AppFlags flags)
	{
		if (!flags.Has(AppFlags.NoHeaderLog))
			Log.Info("Graphics Driver: Headless");
	}

	internal override void DestroyDevice() => disposed = true;
	internal override void Shutdown() { }
	internal override void WindowCreated(Window window) { }
	internal override void WindowDestroyed(Window window) { }
	internal override void Present() { }
	internal override ResourceHandle CreateTexture(string? name, int width, int height, int layers, TextureFormat format, TextureFlags flags, SampleCount sampleCount, nint? targetBinding)
		=> new(new nint(Interlocked.Increment(ref nextResourceId)));

	internal override void SetTextureData(ResourceHandle texture, int layer, nint data, int length, RectInt destRegion) { }
	internal override void GetTextureData(ResourceHandle texture, nint data, int length, RectInt sourceRegion) { }
	internal override void BlitTexture(ResourceHandle sourceTexture, RectInt sourceRegion, ResourceHandle destTexture, RectInt destRegion, TextureFilter filter) { }

	internal override ResourceHandle CreateTarget(int width, int height)
		=> new(new nint(Interlocked.Increment(ref nextResourceId)));

	internal override ResourceHandle CreateShader(Shader shader, byte[] code, string entryPoint)
		=> new(new nint(Interlocked.Increment(ref nextResourceId)));

	internal override ResourceHandle CreateBuffer(string? name, BufferType type, IndexFormat format)
		=> new(new nint(Interlocked.Increment(ref nextResourceId)));

	internal override void UploadBufferData(ResourceHandle buffer, nint data, int dataSize, int dataDestOffset) { }
	internal override void DownloadBufferData(ResourceHandle buffer, int sourceOffsetBytes, int lengthBytes, int slot) { }
	internal override bool TryReadBufferDownload(int slot, nint data, int length) => false;
	internal override void DestroyResource(ResourceHandle resource) { }
	internal override void PerformDraw(DrawCommand command) { }
	internal override void PerformDispatch(ComputeCommand command) { }

	internal override void Clear(IDrawableTarget target, ReadOnlySpan<Color> color, float depth, int stencil, ClearMask mask) { }

	public override bool IsTextureFormatSupported(TextureFormat format) => false;
	public override bool IsTextureMultiSampleSupported(TextureFormat format, SampleCount sampleCount) => false;
}
