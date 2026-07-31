using System.Diagnostics.CodeAnalysis;

namespace Foster.Framework;

/// <summary>
/// Describes a Vertex Format used in rendering Meshes.
/// </summary>
public readonly struct VertexFormat
{
	public readonly record struct Element(
		int Index,
		VertexType Type,
		bool Normalized = true
	);

// iOS interpreter/browser WASM mode does not reliably handle StackList32/InlineArray storage here;
// keep the normal value-type path everywhere else.
#if IOS_INTERP || BROWSER
	private readonly Element[] elements;
#else
	public readonly StackList32<Element> Elements;
#endif
	public readonly int Stride;
	public readonly int ElementCount
	{
		get
		{
#if IOS_INTERP || BROWSER
			return elements?.Length ?? 0;
#else
			return Elements.Count;
#endif
		}
	}

	public readonly ReadOnlySpan<Element> ElementSpan
	{
		get
		{
#if IOS_INTERP || BROWSER
			return elements ?? [];
#else
			return Elements.Span;
#endif
		}
	}

	public VertexFormat(in ReadOnlySpan<Element> elements, int stride = 0)
	{
#if IOS_INTERP || BROWSER
		var computedStride = 0;
		foreach (var it in elements)
			computedStride += it.Type.SizeInBytes();

		this.elements = elements.ToArray();
		Stride = stride != 0 ? stride : computedStride;
#else
		foreach (var it in elements)
		{
			Elements.Add(it);
			Stride += it.Type.SizeInBytes();
		}

		if (stride != 0)
			Stride = stride;
#endif
	}

	public VertexFormat(in StackList32<Element> elements, int stride = 0)
	{
#if IOS_INTERP || BROWSER
		this.elements = elements.Span.ToArray();
		Stride = stride;
		if (Stride == 0)
		{
			foreach (var it in elements)
				Stride += it.Type.SizeInBytes();
		}
#else
		Elements = elements;
		if (stride != 0)
		{
			Stride = stride;
			return;
		}

		foreach (var it in elements)
			Stride += it.Type.SizeInBytes();
#endif
	}

	public static VertexFormat Create<T>(params Element[] elements) where T : struct
		=> new(elements, System.Runtime.CompilerServices.Unsafe.SizeOf<T>());

	public static bool operator ==(VertexFormat a, VertexFormat b)
		=> a.Stride == b.Stride && a.ElementSpan.SequenceEqual(b.ElementSpan);

	public static bool operator !=(VertexFormat a, VertexFormat b)
		=> a.Stride != b.Stride || !a.ElementSpan.SequenceEqual(b.ElementSpan);

	public override bool Equals([NotNullWhen(true)] object? obj)
		=> obj is VertexFormat f && f == this;

	public override int GetHashCode()
	{
		var hash = Stride.GetHashCode();
		foreach (var it in ElementSpan)
			hash = HashCode.Combine(hash, it);
		return hash;
	}
}
