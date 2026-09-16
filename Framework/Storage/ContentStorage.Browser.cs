#if BROWSER
using System.Runtime.Versioning;

namespace Foster.Framework;

[UnsupportedOSPlatform("browser")]
public sealed class ContentStorage : StorageContainer
{
	ContentStorage() { }

	public override bool Writable => throw Unsupported();
	internal bool Ready => throw Unsupported();

	internal static ContentStorage OpenUserStorage(string name) => throw Unsupported();
	internal static ContentStorage OpenTitleStorage(string? path) => throw Unsupported();

	public override bool FileExists(string path) => throw Unsupported();
	public override bool DirectoryExists(string path) => throw Unsupported();
	public override Stream OpenRead(string path) => throw Unsupported();
	public override IEnumerable<string> EnumerateDirectory(string? path = null, string? searchPattern = null,
		SearchOption searchOption = SearchOption.TopDirectoryOnly) => throw Unsupported();
	public override void Dispose(bool disposing) { }

	static PlatformNotSupportedException Unsupported()
		=> new("SDL content storage is not available in the browser");
}
#endif
