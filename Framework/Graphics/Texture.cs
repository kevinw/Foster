using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Foster.Framework;

/// <summary>
/// A 2D Texture used for Rendering
/// </summary>
public class Texture : IGraphicResource
{
	/// <summary>
	/// The GraphicsDevice this Texture was created in
	/// </summary>
	public readonly GraphicsDevice GraphicsDevice;

	/// <summary>
	/// Optional Texture Name
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// If the Texture has been disposed
	/// </summary>
	public bool IsDisposed => disposed || GraphicsDevice.Disposed;

	/// <summary>
	/// Gets the Width of the Texture
	/// </summary>
	public readonly int Width;

	/// <summary>
	/// Gets the Height of the Texture
	/// </summary>
	public readonly int Height;

	/// <summary>
	/// Gets the Size (Width, Height) of the Texture
	/// </summary>
	public Point2 Size => new(Width, Height);

	/// <summary>
	/// The number of array layers in the Texture. This is 1 for an ordinary 2D
	/// Texture, and greater than 1 for a 2D array Texture (each layer is a
	/// separate <see cref="Width"/> x <see cref="Height"/> image sampled with a
	/// layer index). Array textures are created via the array constructor and
	/// have each layer uploaded independently with <see cref="SetData{T}(int, ReadOnlySpan{T})"/>.
	/// </summary>
	public readonly int Layers;

	/// <summary>
	/// Whether this is a 2D array Texture (<see cref="Layers"/> &gt; 1).
	/// </summary>
	public bool IsArray => Layers > 1;

	/// <summary>
	/// The Texture Data Format
	/// </summary>
	public readonly TextureFormat Format;

	/// <summary>
	/// The Texture Sample Count. This is always <see cref="SampleCount.One"/> unless created as a <see cref="Target"/> attachment.
	/// </summary>
	public readonly SampleCount SampleCount;

	/// <summary>
	/// If this Texture is an Attachment for a Render Target.
	/// </summary>
	public readonly bool IsTargetAttachment;

	/// <summary>
	/// The Memory Size of the Texture, in bytes
	/// </summary>
	public int MemorySize => Width * Height * Layers * Format.Size();

	internal readonly GraphicsDevice.ResourceHandle Resource;

	private bool disposed;

	public Texture(GraphicsDevice graphicsDevice, int width, int height, TextureFormat format = TextureFormat.Color, TextureFlags flags = TextureFlags.None, string? name = null)
		: this(graphicsDevice, width, height, layers: 1, format, flags, SampleCount.One, targetBinding: null, name) {}

	/// <summary>
	/// Creates a 2D array Texture with the given number of layers. Each layer is
	/// a separate <paramref name="width"/> x <paramref name="height"/> image;
	/// upload each one with <see cref="SetData{T}(int, ReadOnlySpan{T})"/> and
	/// sample it in a shader declaring a <c>Texture2DArray</c> with a layer index.
	/// </summary>
	public Texture(GraphicsDevice graphicsDevice, int width, int height, int layers, TextureFormat format = TextureFormat.Color, TextureFlags flags = TextureFlags.None, string? name = null)
		: this(graphicsDevice, width, height, layers, format, flags, SampleCount.One, targetBinding: null, name) {}

	public Texture(GraphicsDevice graphicsDevice, int width, int height, ReadOnlySpan<Color> pixels, TextureFlags flags = TextureFlags.None, string? name = null)
		: this(graphicsDevice, width, height, TextureFormat.Color, flags, name) => SetData<Color>(pixels);

	public Texture(GraphicsDevice graphicsDevice, int width, int height, ReadOnlySpan<byte> pixels, TextureFlags flags = TextureFlags.None, string? name = null)
		: this(graphicsDevice, width, height, TextureFormat.Color, flags, name) => SetData<byte>(pixels);

	public Texture(GraphicsDevice graphicsDevice, Image image, TextureFlags flags, string? name = null)
		: this(graphicsDevice, image.Width, image.Height, TextureFormat.Color, flags, name) => SetData<Color>(image.Data);

	public Texture(GraphicsDevice graphicsDevice, Image image, string? name = null)
		: this(graphicsDevice, image.Width, image.Height, TextureFormat.Color, TextureFlags.None, name) => SetData<Color>(image.Data);

	internal Texture(GraphicsDevice graphicsDevice, int width, int height, int layers, TextureFormat format, TextureFlags flags, SampleCount sampleCount, Target? targetBinding, string? name)
	{
		GraphicsDevice = graphicsDevice;

		if (width <= 0 || height <= 0)
			throw new Exception("Texture must have a size larger than 0");

		if (layers <= 0)
			throw new Exception("Texture must have at least one layer");

		if (layers > 1 && targetBinding != null)
			throw new Exception("Array textures cannot be used as Render Target attachments");

		Resource = graphicsDevice.CreateTexture(name, width, height, layers, format, flags, sampleCount, targetBinding?.Resource);
		Name = name ?? string.Empty;
		Width = width;
		Height = height;
		Layers = layers;
		Format = format;
		SampleCount = sampleCount;
		IsTargetAttachment = targetBinding != null;
	}

	~Texture() => Dispose(false);

	/// <summary>
	/// Sets the Texture data from the given buffer
	/// </summary>
	public void SetData<T>(ReadOnlySpan<T> data) where T : struct
		=> SetData(0, data, new RectInt(0, 0, Width, Height));

	/// <summary>
	/// Sets the Texture data in a region from the given buffer
	/// </summary>
	public void SetData<T>(ReadOnlySpan<T> data, RectInt destRegion) where T : struct
		=> SetData(0, data, destRegion);

	/// <summary>
	/// Sets the data of a single array layer from the given buffer. For an
	/// ordinary (non-array) Texture, only layer 0 is valid.
	/// </summary>
	public void SetData<T>(int layer, ReadOnlySpan<T> data) where T : struct
		=> SetData(layer, data, new RectInt(0, 0, Width, Height));

	/// <summary>
	/// Sets the Texture data in a region of a single array layer from the given
	/// buffer. For an ordinary (non-array) Texture, only layer 0 is valid.
	/// </summary>
	public unsafe void SetData<T>(int layer, ReadOnlySpan<T> data, RectInt destRegion) where T : struct
	{
		if (IsDisposed)
			throw new Exception("Resource is Disposed");

		if (layer < 0 || layer >= Layers)
			throw new Exception("Layer is out of range");

		if (destRegion.Left < 0 || destRegion.Top < 0 || destRegion.Bottom > Height || destRegion.Right > Width)
			throw new Exception("Destination region is out of range");

		int dataLength = Unsafe.SizeOf<T>() * data.Length;

		if (dataLength < destRegion.Width * destRegion.Height * Format.Size())
			throw new Exception("Data Buffer is smaller than the Size of the Texture Destination");

		fixed (byte* ptr = MemoryMarshal.AsBytes(data))
		{
			GraphicsDevice.SetTextureData(Resource, layer, new nint(ptr), dataLength, destRegion);
		}
	}

	/// <summary>
	/// Writes the Texture data to the given buffer
	/// </summary>
	public void GetData<T>(Span<T> data) where T : struct
		=> GetData(data, new RectInt(0, 0, Width, Height));

	/// <summary>
	/// Writes the Texture data from a region to the given buffer
	/// </summary>
	public unsafe void GetData<T>(Span<T> data, RectInt sourceRegion) where T : struct
	{
		if (IsDisposed)
			throw new Exception("Resource is Disposed");

		if (sourceRegion.Left < 0 || sourceRegion.Top < 0 || sourceRegion.Bottom > Height || sourceRegion.Right > Width)
			throw new Exception("Source region is out of range");

		int dataLength = Unsafe.SizeOf<T>() * data.Length;

		if (dataLength < sourceRegion.Width * sourceRegion.Height * Format.Size())
			throw new Exception("Data Buffer is smaller than the Size of the Texture Source");

		fixed (byte* ptr = MemoryMarshal.AsBytes(data))
		{
			GraphicsDevice.GetTextureData(Resource, new nint(ptr), dataLength, sourceRegion);
		}
	}

	/// <summary>
	/// Asynchronously requests a copy of the entire texture back to the CPU.
	/// Non-stalling: poll <see cref="TextureDownload{T}.TryRead"/> on a later
	/// frame (at least one later) to retrieve it. See the region overload for
	/// details on reusing a slot.
	/// </summary>
	public TextureDownload<T> RequestDownload<T>(TextureDownload<T> download = default) where T : unmanaged
		=> RequestDownload(new RectInt(0, 0, Width, Height), download);

	/// <summary>
	/// Asynchronously requests a copy of a region of the texture back to the
	/// CPU. This is non-stalling, but the result is not available immediately -
	/// call <see cref="TextureDownload{T}.TryRead"/> on a later frame (at least
	/// one frame later) to retrieve it.
	///
	/// Pass the handle returned from a previous call back in via
	/// <paramref name="download"/> to reuse its slot (and staging buffer);
	/// otherwise pass <c>default</c> to allocate a new one.
	/// </summary>
	public TextureDownload<T> RequestDownload<T>(RectInt sourceRegion, TextureDownload<T> download = default) where T : unmanaged
	{
		if (IsDisposed)
			throw new Exception("Resource is Disposed");
		if (sourceRegion.Left < 0 || sourceRegion.Top < 0 || sourceRegion.Bottom > Height || sourceRegion.Right > Width)
			throw new Exception("Source region is out of range");

		var slot = download.Slot != 0 ? download.Slot : GraphicsDevice.AllocateDownloadSlot();
		GraphicsDevice.DownloadTextureData(Resource, sourceRegion, Format.Size(), slot);
		return new TextureDownload<T>(GraphicsDevice, slot);
	}

	/// <summary>
	/// Blits the contents of this Texture to another Texture
	/// </summary>
	public void Blit(RectInt sourceRect, Texture destination, RectInt destinationRect, TextureFilter filter)
	{
		GraphicsDevice.BlitTexture(Resource, sourceRect, destination.Resource, destinationRect, filter);
	}

	/// <summary>
	/// Blits the contents of this Texture to another Texture
	/// </summary>
	public void Blit(Texture destination, TextureFilter filter)
	{
		Blit(new(Size), destination, new(destination.Size), filter);
	}

	/// <summary>
	/// Clones this Texture and creates a new one with the same pixel data
	/// </summary>
	public Texture Clone()
	{
		var clone = new Texture(GraphicsDevice, Width, Height, Format);
		Blit(clone, TextureFilter.Nearest);
		return clone;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	private void Dispose(bool disposing)
	{
		if (!disposed)
		{
			// Targets should dispose their Texture Attachments
			if (!IsTargetAttachment)
				GraphicsDevice.DestroyResource(Resource);

			disposed = true;
		}
	}
}

/// <summary>
/// A handle to an in-flight or completed asynchronous GPU texture download,
/// returned by <see cref="Texture.RequestDownload{T}(TextureDownload{T})"/>. A
/// default-initialized handle has no associated download and
/// <see cref="TryRead"/> always returns false for it.
/// </summary>
public readonly struct TextureDownload<T>(GraphicsDevice graphicsDevice, int downloadSlot) where T : unmanaged
{
	internal readonly int Slot = downloadSlot;

	/// <summary>
	/// Attempts to read back the most recently downloaded data for this handle,
	/// tightly packed into <paramref name="dest"/>. Returns false if no download
	/// has completed for this handle yet (e.g. the GPU copy and async map are
	/// still in flight) - retry on a later frame.
	/// </summary>
	public unsafe bool TryRead(Span<T> dest)
	{
		if (Slot == 0)
			return false;

		fixed (T* ptr = dest)
			return graphicsDevice.TryReadTextureDownload(Slot, new nint(ptr), dest.Length * Unsafe.SizeOf<T>());
	}
}
