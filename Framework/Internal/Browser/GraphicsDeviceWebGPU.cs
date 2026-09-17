#if BROWSER
using System.Runtime.InteropServices;

namespace Foster.Framework;

internal sealed class GraphicsDeviceWebGPU(App app) : GraphicsDevice(app)
{
	private abstract class Resource
	{
		public int BrowserHandle;
	}

	private sealed class TextureResource : Resource
	{
		public int Width;
		public int Height;
		public int Layers;
		public TextureFormat Format;
		public TargetResource? Target;
	}

	private sealed class ShaderResource : Resource {}

	private sealed class TargetResource : Resource
	{
		public int Width;
		public int Height;
		public readonly List<TextureResource> Attachments = [];
	}

	private sealed class BufferResource : Resource
	{
		public byte[] Data = [];
		public byte[] UploadScratch = [];
		public int LogicalSize;
	}

	private readonly Dictionary<nint, Resource> resources = [];
	private long nextResourceId;
	private bool frameBegun;
	private bool disposed = true;
	private readonly int[] drawTextures = new int[16];
	private readonly int[] drawSamplerFilters = new int[16];
	private readonly int[] drawSamplerWrapX = new int[16];
	private readonly int[] drawSamplerWrapY = new int[16];
	// Vertex-stage textures/samplers + storage buffers live in bind group 0
	// (distinct from the fragment samplers above, which are group 2).
	private readonly int[] drawVertexTextures = new int[16];
	private readonly int[] drawVertexSamplerFilters = new int[16];
	private readonly int[] drawVertexSamplerWrapX = new int[16];
	private readonly int[] drawVertexSamplerWrapY = new int[16];
	private byte[] drawVertexUniform = Array.Empty<byte>();
	private byte[] drawFragmentUniform = Array.Empty<byte>();
	private byte[] drawComputeUniform = Array.Empty<byte>();
	private readonly int[][] computeReadOnlyBuffers = new int[9][];
	private readonly int[][] computeReadWriteBuffers = new int[5][];
	private readonly int[][] computeReadWriteTextures = new int[5][];
	private readonly int[] drawVertexLocations = new int[32];
	private readonly int[] drawVertexOffsets = new int[32];
	private readonly int[] drawVertexTypes = new int[32];
	private readonly int[] drawVertexNormalized = new int[32];

	public override GraphicsDriver Driver => GraphicsDriver.WebGPU;
	public override bool Disposed => disposed;
	public override bool VSync { get; set; } = true;
	public override void InsertDebugLabel(string text) { }

	internal override void CreateDevice(in AppFlags flags)
	{
		BrowserWebGPU.Initialize("#foster-canvas");
		disposed = false;
		if (!flags.Has(AppFlags.NoHeaderLog))
		{
			var adapter = BrowserWebGPU.GetAdapterInfo();
			Log.Info($"Graphics Driver: WebGPU{(adapter.Length > 0 ? $" | {adapter}" : "")}");
		}
	}

	internal override void DestroyDevice()
	{
		resources.Clear();
		disposed = true;
	}

	internal override void Shutdown() {}
	internal override void WindowCreated(Window window) {}
	internal override void WindowDestroyed(Window window) {}
	internal override void Present()
	{
		if (frameBegun)
			BrowserWebGPU.Present();
		frameBegun = false;
	}

	internal override void SubmitPendingCommandsCore()
	{
		if (frameBegun)
			BrowserWebGPU.Submit();
		frameBegun = false;
	}

	internal override ResourceHandle CreateTexture(string? name, int width, int height, int layers, TextureFormat format, TextureFlags flags, SampleCount sampleCount, nint? targetBinding)
	{
		if (!format.IsColorFormat())
			throw new NotSupportedException($"WebGPU prototype does not support depth/stencil textures yet. Requested {format}.");
		if (sampleCount != SampleCount.One)
			throw new NotSupportedException("WebGPU prototype does not support multisampled textures yet.");

		var target = targetBinding is { } binding && binding != nint.Zero
			? Find<TargetResource>(new ResourceHandle(binding))
			: null;
		var computeUsage = (flags.Has(TextureFlags.ComputeRead) ? 1 : 0) | (flags.Has(TextureFlags.ComputeWrite) ? 2 : 0);
		var resource = new TextureResource
		{
			BrowserHandle = BrowserWebGPU.CreateTexture(name ?? string.Empty, width, height, layers, WebGPUTextureFormat(format), target?.BrowserHandle ?? 0, computeUsage),
			Width = width,
			Height = height,
			Layers = layers,
			Format = format,
			Target = target,
		};
		target?.Attachments.Add(resource);
		return AddResource(resource);
	}

	internal override unsafe void SetTextureData(ResourceHandle texture, int layer, nint data, int length, RectInt destRegion)
	{
		var resource = Find<TextureResource>(texture);
		var bytes = new byte[length];
		new ReadOnlySpan<byte>((void*)data, length).CopyTo(bytes);
		BrowserWebGPU.UploadTexture(resource.BrowserHandle, layer, bytes, destRegion.X, destRegion.Y, destRegion.Width, destRegion.Height, resource.Format.Size());
	}

	internal override void GetTextureData(ResourceHandle texture, nint data, int length, RectInt sourceRegion)
		=> throw new NotSupportedException(
			"WebGPU cannot read texture data back synchronously on the browser main thread. " +
			"Use Texture.RequestDownload(...) and poll TextureDownload<T>.TryRead(...) on a later frame instead.");

	internal override void DownloadTextureData(ResourceHandle texture, RectInt sourceRegion, int bytesPerPixel, int slot)
	{
		var resource = Find<TextureResource>(texture);
		BrowserWebGPU.RequestTextureDownload(resource.BrowserHandle, sourceRegion.X, sourceRegion.Y, sourceRegion.Width, sourceRegion.Height, bytesPerPixel, slot);
		frameBegun = true;
	}

	internal override unsafe bool TryReadTextureDownload(int slot, nint data, int length)
	{
		var bytes = BrowserWebGPU.TryReadTextureDownload(slot, length);
		if (bytes == null)
			return false;

		new ReadOnlySpan<byte>(bytes).CopyTo(new Span<byte>((void*)data, length));
		return true;
	}

	internal override void BlitTexture(ResourceHandle sourceTexture, RectInt sourceRegion, ResourceHandle destTexture, RectInt destRegion, TextureFilter filter)
		=> throw new NotSupportedException("WebGPU prototype does not support texture blits yet.");

	internal override ResourceHandle CreateTarget(int width, int height)
		=> AddResource(new TargetResource { BrowserHandle = BrowserWebGPU.CreateTarget(string.Empty, width, height), Width = width, Height = height });

	internal override ResourceHandle CreateShader(Shader shader, byte[] code, string entryPoint)
	{
		var text = System.Text.Encoding.UTF8.GetString(code);
		var resource = new ShaderResource
		{
			BrowserHandle = BrowserWebGPU.CreateShader(shader.Name, text, entryPoint),
		};
		return AddResource(resource);
	}

	internal override ResourceHandle CreateBuffer(string? name, BufferType type, IndexFormat format)
	{
		var usage = type switch
		{
			BufferType.Vertex => 1,
			BufferType.Index => 2,
			BufferType.Storage or BufferType.Compute => 4,
			_ => 0,
		};
		return AddResource(new BufferResource { BrowserHandle = BrowserWebGPU.CreateBuffer(name ?? string.Empty, usage, (int)format) });
	}

	internal override unsafe void UploadBufferData(ResourceHandle buffer, nint data, int dataSize, int dataDestOffset)
	{
		var resource = Find<BufferResource>(buffer);
		var required = dataDestOffset + dataSize;
		var requiredCapacity = AlignTo(required, 4);
		if (resource.Data.Length < requiredCapacity)
			Array.Resize(ref resource.Data, requiredCapacity);
		resource.LogicalSize = Math.Max(resource.LogicalSize, required);

		if (data != nint.Zero && dataSize > 0)
			new ReadOnlySpan<byte>((void*)data, dataSize).CopyTo(resource.Data.AsSpan(dataDestOffset));

		if (dataDestOffset == 0 && dataSize > 0)
		{
			var uploadSize = AlignTo(dataSize, 4);
			if (resource.UploadScratch.Length < uploadSize)
				Array.Resize(ref resource.UploadScratch, uploadSize);
			resource.Data.AsSpan(0, dataSize).CopyTo(resource.UploadScratch);
			BrowserWebGPU.UploadBuffer(resource.BrowserHandle, resource.UploadScratch, 0, dataSize, required);
		}
		else
		{
			BrowserWebGPU.UploadBuffer(resource.BrowserHandle, resource.Data, 0, resource.LogicalSize, resource.LogicalSize);
		}
	}

	internal override void DownloadBufferData(ResourceHandle buffer, int sourceOffsetBytes, int lengthBytes, int slot)
	{
		var resource = Find<BufferResource>(buffer);
		BrowserWebGPU.RequestBufferDownload(resource.BrowserHandle, sourceOffsetBytes, lengthBytes, slot);
		frameBegun = true;
	}

	internal override unsafe bool TryReadBufferDownload(int slot, nint data, int length)
	{
		var bytes = BrowserWebGPU.TryReadBufferDownload(slot, length);
		if (bytes == null)
			return false;

		new ReadOnlySpan<byte>(bytes).CopyTo(new Span<byte>((void*)data, length));
		return true;
	}

	internal override void DestroyResource(ResourceHandle resource)
	{
		if (!resources.Remove(resource.Id, out var found))
			return;
		if (found is TargetResource target)
			foreach (var attachment in target.Attachments)
				if (attachment.BrowserHandle != 0)
					BrowserWebGPU.DestroyResource(attachment.BrowserHandle);
		if (found.BrowserHandle != 0)
			BrowserWebGPU.DestroyResource(found.BrowserHandle);
	}

	internal override void Clear(IDrawableTarget target, ReadOnlySpan<Color> color, float depth, int stencil, ClearMask mask)
	{
		if (!mask.Has(ClearMask.Color) || color.Length <= 0)
			return;
		BrowserWebGPU.ClearTarget(TargetHandle(target), color[0].R / 255.0f, color[0].G / 255.0f, color[0].B / 255.0f, color[0].A / 255.0f);
		frameBegun = true;
	}

	internal override void PerformDraw(DrawCommand command)
	{
		if (command.IndexBuffer == null)
			throw new NotSupportedException("WebGPU prototype currently expects indexed draws.");

		var vertexShader = Find<ShaderResource>(command.VertexShader!.Resource);
		var fragmentShader = Find<ShaderResource>(command.FragmentShader!.Resource);
		var vertexBuffer = Find<BufferResource>(command.VertexBuffers[0].Buffer.Resource);
		var indexBuffer = Find<BufferResource>(command.IndexBuffer.Resource);
		var vertexFormat = command.VertexBuffers[0].Buffer.Format;
		var vertexElementCount = CopyVertexFormat(vertexFormat, drawVertexLocations, drawVertexOffsets, drawVertexTypes, drawVertexNormalized);
		var samplerCount = Math.Min(command.FragmentShader!.SamplerCount, command.FragmentSamplers.Count);
		for (var i = 0; i < samplerCount; i++)
		{
			var sampler = command.FragmentSamplers[i];
			drawTextures[i] = sampler.Texture is { } sampled ? Find<TextureResource>(sampled.Resource).BrowserHandle : 0;
			drawSamplerFilters[i] = sampler.Sampler.Filter switch { TextureFilter.Linear => 1, _ => 0 };
			drawSamplerWrapX[i] = sampler.Sampler.WrapX switch { TextureWrap.Repeat => 1, TextureWrap.MirroredRepeat => 2, _ => 0 };
			drawSamplerWrapY[i] = sampler.Sampler.WrapY switch { TextureWrap.Repeat => 1, TextureWrap.MirroredRepeat => 2, _ => 0 };
		}
		// Vertex-stage group 0: sampled textures (low t-registers), samplers at
		// 100+i, then vertex storage buffers offset after the textures.
		var vertexSamplerCount = Math.Min(Math.Min(command.VertexShader!.SamplerCount, command.VertexSamplers.Count), drawVertexTextures.Length);
		for (var i = 0; i < vertexSamplerCount; i++)
		{
			var sampler = command.VertexSamplers[i];
			drawVertexTextures[i] = sampler.Texture is { } sampled ? Find<TextureResource>(sampled.Resource).BrowserHandle : 0;
			drawVertexSamplerFilters[i] = sampler.Sampler.Filter switch { TextureFilter.Linear => 1, _ => 0 };
			drawVertexSamplerWrapX[i] = sampler.Sampler.WrapX switch { TextureWrap.Repeat => 1, TextureWrap.MirroredRepeat => 2, _ => 0 };
			drawVertexSamplerWrapY[i] = sampler.Sampler.WrapY switch { TextureWrap.Repeat => 1, TextureWrap.MirroredRepeat => 2, _ => 0 };
		}
		var vertexStorageBuffers = new int[command.VertexStorageBuffers.Count];
		for (var i = 0; i < vertexStorageBuffers.Length; i++)
			vertexStorageBuffers[i] = Find<BufferResource>(command.VertexStorageBuffers[i]!.Resource).BrowserHandle;

		var vertexUniformLength = CopyUniform(command.VertexUniformBuffers.Count > 0 ? command.VertexUniformBuffers[0] : null, ref drawVertexUniform);
		var fragmentUniformLength = CopyUniform(command.FragmentUniformBuffers.Count > 0 ? command.FragmentUniformBuffers[0] : null, ref drawFragmentUniform);
		var viewport = command.Viewport ?? new RectInt(0, 0, command.Target.WidthInPixels, command.Target.HeightInPixels);
		var scissor = command.Scissor ?? new RectInt(0, 0, 0, 0);

		BrowserWebGPU.DrawBatch(
			vertexShader.BrowserHandle,
			fragmentShader.BrowserHandle,
			vertexBuffer.BrowserHandle,
			indexBuffer.BrowserHandle,
			vertexFormat.Stride,
			drawVertexLocations,
			drawVertexOffsets,
			drawVertexTypes,
			drawVertexNormalized,
			vertexElementCount,
			TargetHandle(command.Target),
			drawTextures,
			drawSamplerFilters,
			drawSamplerWrapX,
			drawSamplerWrapY,
			samplerCount,
			drawVertexTextures,
			drawVertexSamplerFilters,
			drawVertexSamplerWrapX,
			drawVertexSamplerWrapY,
			vertexSamplerCount,
			vertexStorageBuffers,
			drawVertexUniform,
			vertexUniformLength,
			drawFragmentUniform,
			fragmentUniformLength,
			(int)command.BlendMode.ColorOperation,
			(int)command.BlendMode.ColorSource,
			(int)command.BlendMode.ColorDestination,
			(int)command.BlendMode.AlphaOperation,
			(int)command.BlendMode.AlphaSource,
			(int)command.BlendMode.AlphaDestination,
			(int)command.BlendMode.Mask,
			viewport.X,
			viewport.Y,
			viewport.Width,
			viewport.Height,
			scissor.X,
			scissor.Y,
			scissor.Width,
			scissor.Height,
			command.IndexOffset,
			command.IndexCount,
			command.VertexOffset,
			command.InstanceCount,
			command.Target.WidthInPixels,
			command.Target.HeightInPixels);
		frameBegun = true;
	}

	internal override void PerformDispatch(ComputeCommand command)
	{
		var shader = Find<ShaderResource>(command.Shader!.Resource);

		// Sampled textures + samplers share group 0 with the read-only storage
		// buffers: the shader must declare textures on the low t-registers (t0..)
		// and read-only buffers after them, so on the JS side textures bind at
		// [0..T-1], samplers at [100..], and read-only buffers are offset by T.
		var samplerCount = Math.Min(command.Samplers.Count, drawTextures.Length);
		for (var i = 0; i < samplerCount; i++)
		{
			var sampler = command.Samplers[i];
			drawTextures[i] = sampler.Texture is { } sampled ? Find<TextureResource>(sampled.Resource).BrowserHandle : 0;
			drawSamplerFilters[i] = sampler.Sampler.Filter switch { TextureFilter.Linear => 1, _ => 0 };
			drawSamplerWrapX[i] = sampler.Sampler.WrapX switch { TextureWrap.Repeat => 1, TextureWrap.MirroredRepeat => 2, _ => 0 };
			drawSamplerWrapY[i] = sampler.Sampler.WrapY switch { TextureWrap.Repeat => 1, TextureWrap.MirroredRepeat => 2, _ => 0 };
		}

		var readOnlyBuffers = Scratch(computeReadOnlyBuffers, command.ReadOnlyStorageBuffers.Count);
		for (var i = 0; i < readOnlyBuffers.Length; i++)
			readOnlyBuffers[i] = Find<BufferResource>(command.ReadOnlyStorageBuffers[i]!.Resource).BrowserHandle;

		var readWriteBuffers = Scratch(computeReadWriteBuffers, command.ReadWriteStorageBuffers.Count);
		for (var i = 0; i < readWriteBuffers.Length; i++)
			readWriteBuffers[i] = Find<BufferResource>(command.ReadWriteStorageBuffers[i]!.Resource).BrowserHandle;

		var readWriteTextures = Scratch(computeReadWriteTextures, command.ReadWriteStorageTextures.Count);
		for (var i = 0; i < readWriteTextures.Length; i++)
			readWriteTextures[i] = Find<TextureResource>(command.ReadWriteStorageTextures[i]!.Resource).BrowserHandle;

		var uniformLength = CopyUniform(command.UniformBuffers.Count > 0 ? command.UniformBuffers[0] : null, ref drawComputeUniform);

		BrowserWebGPU.DispatchCompute(
			shader.BrowserHandle,
			drawTextures,
			drawSamplerFilters,
			drawSamplerWrapX,
			drawSamplerWrapY,
			samplerCount,
			readOnlyBuffers,
			readWriteBuffers,
			readWriteTextures,
			drawComputeUniform,
			uniformLength,
			command.GroupCountX,
			command.GroupCountY,
			command.GroupCountZ);

		frameBegun = true;
	}

	static int[] Scratch(int[][] cache, int count) => cache[count] ??= new int[count];

	public override bool IsTextureFormatSupported(TextureFormat format)
		=> format is TextureFormat.Color or TextureFormat.R8G8B8A8 or TextureFormat.R8 or TextureFormat.R8G8;

	public override bool IsTextureMultiSampleSupported(TextureFormat format, SampleCount sampleCount)
		=> sampleCount == SampleCount.One && IsTextureFormatSupported(format);

	private ResourceHandle AddResource(Resource resource)
	{
		var id = new nint(++nextResourceId);
		resources.Add(id, resource);
		return id;
	}

	private T Find<T>(ResourceHandle handle) where T : Resource
	{
		if (resources.TryGetValue(handle.Id, out var resource) && resource is T typed)
			return typed;
		throw new Exception("Invalid WebGPU resource handle");
	}

	private int TargetHandle(IDrawableTarget target)
		=> target.Surface is Target renderTarget ? Find<TargetResource>(renderTarget.Resource).BrowserHandle : 0;

	private static int AlignTo(int value, int alignment)
		=> (value + alignment - 1) & ~(alignment - 1);

	private static int CopyUniform(UniformBuffer? uniform, ref byte[] scratch)
	{
		if (uniform == null)
			return 0;

		var span = uniform.Get();
		if (span.Length <= 0)
			return 0;

		if (scratch.Length < span.Length)
			Array.Resize(ref scratch, span.Length);
		span.CopyTo(scratch);
		return span.Length;
	}

	private static int WebGPUTextureFormat(TextureFormat format)
		=> format switch
		{
			TextureFormat.R8G8B8A8 => 0,
			TextureFormat.R8 => 1,
			TextureFormat.R8G8 => 2,
			_ => throw new NotSupportedException($"Unsupported WebGPU texture format {format}.")
		};

	private static int CopyVertexFormat(VertexFormat format, int[] locations, int[] offsets, int[] types, int[] normalized)
	{
		var offset = 0;
		var elements = format.ElementSpan[..format.ElementCount];
		for (var i = 0; i < elements.Length; i++)
		{
			var element = elements[i];
			locations[i] = element.Index;
			offsets[i] = offset;
			types[i] = WebGPUVertexType(element.Type);
			normalized[i] = element.Normalized ? 1 : 0;
			offset += element.Type.SizeInBytes();
		}
		return elements.Length;
	}

	private static int WebGPUVertexType(VertexType type)
		=> type switch
		{
			VertexType.Float => 1,
			VertexType.Float2 => 2,
			VertexType.Float3 => 3,
			VertexType.Float4 => 4,
			VertexType.Byte4 => 5,
			VertexType.UByte4 => 6,
			VertexType.Short2 => 7,
			VertexType.UShort2 => 8,
			VertexType.Short4 => 9,
			VertexType.UShort4 => 10,
			_ => throw new NotSupportedException($"Unsupported WebGPU vertex type {type}.")
		};
}
#endif
