namespace Foster.Framework;

/// <summary>
/// A writable Storage Container backed by a real directory on the host file system.
/// Paths given to the container are relative to the root folder.
/// </summary>
public class FolderStorage(string root) : StorageContainer
{
	private readonly string root = Path.GetFullPath(root);

	public override bool Writable => true;

	public string Root => root;

	private string Resolve(string? path)
		=> string.IsNullOrEmpty(path) ? root : Path.Join(root, path);

	public override bool FileExists(string path)
		=> File.Exists(Resolve(path));

	public override bool DirectoryExists(string path)
		=> Directory.Exists(Resolve(path));

	public override Stream OpenRead(string path)
		=> File.OpenRead(Resolve(path));

	public override IEnumerable<string> EnumerateDirectory(string? path = null, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly)
	{
		var directory = Resolve(path);
		if (!Directory.Exists(directory))
			yield break;

		foreach (var entry in Directory.EnumerateFileSystemEntries(directory, searchPattern ?? "*", searchOption))
			yield return Calc.NormalizePath(Path.GetRelativePath(root, entry));
	}

	public override bool CreateDirectory(string path)
		=> Directory.CreateDirectory(Resolve(path)).Exists;

	public override Stream Create(string path)
	{
		var resolved = Resolve(path);
		if (Path.GetDirectoryName(resolved) is { Length: > 0 } directory)
			Directory.CreateDirectory(directory);
		return File.Create(resolved);
	}

	public override bool Remove(string path)
	{
		var resolved = Resolve(path);
		if (File.Exists(resolved))
		{
			File.Delete(resolved);
			return true;
		}
		if (Directory.Exists(resolved))
		{
			Directory.Delete(resolved, recursive: true);
			return true;
		}
		return false;
	}

	public override void Dispose(bool disposing) {}
}
