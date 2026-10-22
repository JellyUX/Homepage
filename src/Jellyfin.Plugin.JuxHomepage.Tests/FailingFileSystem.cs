using Jellyfin.Plugin.JuxHomepage.IO;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// The real file system, made to refuse writes, reads or the creation of a directory on demand: a full or
/// read-only disk, a file locked by another process, or a DataPath the server cannot write to (FIAB-03, H-07).
/// </summary>
internal sealed class FailingFileSystem : IFileSystem
{
    private readonly FileSystem _inner = new();

    public bool FailWrites { get; set; }

    public bool FailCreateDirectory { get; set; }

    public bool FailReads { get; set; }

    /// <summary>Gets or sets the number of existence checks, date reads and file reads.</summary>
    public int Reads { get; set; }

    public bool FileExists(string path)
    {
        Reads++;
        return _inner.FileExists(path);
    }

    public string ReadAllText(string path)
    {
        Reads++;
        if (FailReads)
        {
            throw new IOException("The file is being used by another process.");
        }

        return _inner.ReadAllText(path);
    }

    public void WriteAllText(string path, string contents)
    {
        if (FailWrites)
        {
            throw new IOException("No space left on device.");
        }

        _inner.WriteAllText(path, contents);
    }

    public void Move(string sourceFileName, string destFileName, bool overwrite) => _inner.Move(sourceFileName, destFileName, overwrite);

    public void Delete(string path) => _inner.Delete(path);

    public void CreateDirectory(string path)
    {
        if (FailCreateDirectory)
        {
            throw new UnauthorizedAccessException("Access to the path is denied.");
        }

        _inner.CreateDirectory(path);
    }

    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) => _inner.GetFiles(path, searchPattern, searchOption);

    public DateTime GetLastWriteTimeUtc(string path)
    {
        Reads++;
        return _inner.GetLastWriteTimeUtc(path);
    }
}
