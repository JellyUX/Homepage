namespace Jellyfin.Plugin.JuxHomepage.IO;

/// <summary>
/// Default <see cref="IFileSystem"/> implementation, delegating directly to System.IO.
/// </summary>
public sealed class FileSystem : IFileSystem
{
    /// <inheritdoc/>
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc/>
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc/>
    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);

    /// <inheritdoc/>
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    /// <inheritdoc/>
    public void Move(string sourceFileName, string destFileName, bool overwrite) =>
        File.Move(sourceFileName, destFileName, overwrite);

    /// <inheritdoc/>
    public void Delete(string path) => File.Delete(path);

    /// <inheritdoc/>
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc/>
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) =>
        Directory.GetFiles(path, searchPattern, searchOption);

    /// <inheritdoc/>
    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
}
