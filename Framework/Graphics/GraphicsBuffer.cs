using System.Runtime.InteropServices;

namespace Foster.Framework;

/// <summary>
/// A buffer that stores graphical data used when drawing.
/// </summary>
public abstract class GraphicsBuffer : IGraphicResource
{
	internal readonly int ElementSizeInBytes;
	internal readonly GraphicsDevice.ResourceHandle Resource;
	private bool disposed;

	/// <summary>
	/// The GraphicsDevice this Buffer was created in
	/// </summary>
	public readonly GraphicsDevice GraphicsDevice;

	/// <summary>
	/// Name of the Buffer
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Number of Elements in the Buffer
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// If the Buffer has been disposed
	/// </summary>
	public bool IsDisposed => disposed || GraphicsDevice.Disposed;

	internal GraphicsBuffer(GraphicsDevice graphicsDevice, int elementSizeInBytes, GraphicsDevice.BufferType type, IndexFormat? indexFormat, string? name)
	{
		GraphicsDevice = graphicsDevice;
		Name = name ?? string.Empty;
		ElementSizeInBytes = elementSizeInBytes;
		Resource = graphicsDevice.CreateBuffer(name, type, indexFormat ?? default);
	}

	~GraphicsBuffer()
		=> Dispose();

	/// <summary>
	/// Uploads data to the buffer, and resizes it if required.
	/// </summary>
	public void Upload(nint data, int elementCount, int elementOffset = 0)
	{
		if (IsDisposed)
			throw new Exception("Trying to upload to a disposed DrawBuffer");

		Count = Math.Max(Count, elementOffset + elementCount);
		GraphicsDevice.UploadBufferData(Resource, data, elementCount * ElementSizeInBytes, elementOffset * ElementSizeInBytes);
	}

	/// <summary>
	/// Sets the Buffer Element Count to 0
	/// </summary>
	public void Clear()
	{
		Count = 0;
	}

	/// <summary>
	/// Disposes of the Buffer's resources
	/// </summary>
	public void Dispose()
	{
		if (!disposed)
		{
			GraphicsDevice.DestroyResource(Resource);
			disposed = true;
		}

		GC.SuppressFinalize(this);
	}
}

/// <summary>
/// Holds Vertex Elements for drawing
/// </summary>
public class VertexBuffer(GraphicsDevice graphicsDevice, VertexFormat format, string? name = null)
	: GraphicsBuffer(graphicsDevice, format.Stride, GraphicsDevice.BufferType.Vertex, null, name)
{
	public readonly VertexFormat Format = format;
}

/// <summary>
/// Holds Vertex Elements for drawing
/// </summary>
public class VertexBuffer<T>(GraphicsDevice graphicsDevice, string? name = null)
	: VertexBuffer(graphicsDevice, default(T).Format, name) where T : unmanaged, IVertex
{
	/// <summary>
	/// Uploads data to the buffer, and resizes it if required.
	/// </summary>
	public unsafe void Upload(in ReadOnlySpan<T> data, int offset = 0)
	{
		fixed (T* ptr = data)
			Upload(new nint(ptr), data.Length, offset);
	}
}

/// <summary>
/// Holds Index Elements for drawing
/// </summary>
public class IndexBuffer(GraphicsDevice graphicsDevice, IndexFormat format, string? name = null)
	: GraphicsBuffer(graphicsDevice, format.SizeInBytes(), GraphicsDevice.BufferType.Index, format, name)
{
	/// <summary>
	/// The Index Element Format
	/// </summary>
	public readonly IndexFormat Format = format;
}

/// <summary>
/// Holds Index Elements for drawing
/// </summary>
public class IndexBuffer<T>(GraphicsDevice graphicsDevice, string? name = null)
	: IndexBuffer(graphicsDevice, IndexFormatExt.GetFormatOf<T>(), name) where T : unmanaged
{
	/// <summary>
	/// Uploads data to the buffer, and resizes it if required.
	/// </summary>
	public unsafe void Upload(in ReadOnlySpan<T> data, int offset = 0)
	{
		fixed (T* ptr = data)
			Upload(new nint(ptr), data.Length, offset);
	}
}

/// <summary>
/// Holds Storage Elements for drawing (read-only in shaders)
/// </summary>
public class StorageBuffer : GraphicsBuffer
{
	public StorageBuffer(GraphicsDevice graphicsDevice, int elementSizeInBytes, string? name = null)
		: base(graphicsDevice, elementSizeInBytes, GraphicsDevice.BufferType.Storage, null, name) {}

	internal StorageBuffer(GraphicsDevice graphicsDevice, int elementSizeInBytes, GraphicsDevice.BufferType type, string? name)
		: base(graphicsDevice, elementSizeInBytes, type, null, name) {}
}

/// <summary>
/// Holds Storage Elements for drawing (read-only in shaders)
/// </summary>
public class StorageBuffer<T>(GraphicsDevice graphicsDevice, string? name = null)
	: StorageBuffer(graphicsDevice, Marshal.SizeOf<T>(), name) where T : unmanaged
{
	/// <summary>
	/// Uploads data to the buffer, and resizes it if required.
	/// </summary>
	public unsafe void Upload(in ReadOnlySpan<T> data, int offset = 0)
	{
		fixed (T* ptr = data)
			Upload(new nint(ptr), data.Length, offset);
	}
}

/// <summary>
/// Holds Storage Elements that can be read and written to by compute shaders.
/// Can also be read by non-compute shaders as a storage buffer.
/// </summary>
public class ComputeStorageBuffer(GraphicsDevice graphicsDevice, int elementSizeInBytes, string? name = null)
	: StorageBuffer(graphicsDevice, elementSizeInBytes, GraphicsDevice.BufferType.Compute, name) {}

/// <summary>
/// Holds Storage Elements that can be read and written to by compute shaders.
/// Can also be read by non-compute shaders as a storage buffer.
/// </summary>
public class ComputeStorageBuffer<T>(GraphicsDevice graphicsDevice, string? name = null)
	: ComputeStorageBuffer(graphicsDevice, Marshal.SizeOf<T>(), name) where T : unmanaged
{
	/// <summary>
	/// Uploads data to the buffer, and resizes it if required.
	/// </summary>
	public unsafe void Upload(in ReadOnlySpan<T> data, int offset = 0)
	{
		fixed (T* ptr = data)
			Upload(new nint(ptr), data.Length, offset);
	}

	/// <summary>
	/// Asynchronously requests a copy of a range of elements back to the CPU.
	/// This is non-stalling, but the result is not available immediately -
	/// call <see cref="BufferDownload{T}.TryRead"/> on a later frame (at
	/// least one frame later) to retrieve it.
	///
	/// Pass the handle returned from a previous call back in via
	/// <paramref name="download"/> to reuse its slot (and transfer buffer)
	/// for this new request; otherwise pass <c>default</c> to allocate a new
	/// one. Typical usage is to keep the returned handle in a field and pass
	/// it back in each time:
	/// <code>
	/// download = buffer.RequestDownload(0, count, download);
	/// </code>
	/// </summary>
	public BufferDownload<T> RequestDownload(int elementOffset, int elementCount, BufferDownload<T> download = default)
	{
		var slot = download.Slot != 0 ? download.Slot : GraphicsDevice.AllocateDownloadSlot();
		GraphicsDevice.DownloadBufferData(Resource, elementOffset * ElementSizeInBytes, elementCount * ElementSizeInBytes, slot);
		return new BufferDownload<T>(GraphicsDevice, slot, ElementSizeInBytes);
	}
}

/// <summary>
/// A handle to an in-flight or completed asynchronous GPU buffer download,
/// returned by <see cref="ComputeStorageBuffer{T}.RequestDownload"/>. A
/// default-initialized handle has no associated download and
/// <see cref="TryRead"/> always returns false for it.
/// </summary>
public readonly struct BufferDownload<T>(GraphicsDevice graphicsDevice, int downloadSlot, int elementSizeInBytes) where T : unmanaged
{
	internal readonly int Slot = downloadSlot;

	/// <summary>
	/// Attempts to read back the most recently downloaded data for this
	/// handle. Returns false if no download has ever been requested for this
	/// handle. The data may be stale (from a previous request) if the GPU
	/// hasn't finished the copy yet - wait at least one frame after
	/// requesting before reading.
	/// </summary>
	public unsafe bool TryRead(Span<T> dest)
	{
		if (Slot == 0)
			return false;

		fixed (T* ptr = dest)
			return graphicsDevice.TryReadBufferDownload(Slot, new nint(ptr), dest.Length * elementSizeInBytes);
	}
}