using System.Collections.Concurrent;
using System.Text;
using Jellyfin.Plugin.JuxHomepage.IO;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// A file system held in memory, shared by two sets of services to stand for a server restart; each write moves the
/// file's write time forward, so a reader that memoizes by write time sees every write.
/// </summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly ConcurrentDictionary<string, (string Contents, DateTime WrittenAt)> _files = new(StringComparer.OrdinalIgnoreCase);
    private long _clock;

    public IReadOnlyCollection<string> Paths => [.. _files.Keys];

    public bool FileExists(string path) => _files.ContainsKey(path);

    public string ReadAllText(string path) =>
        _files.TryGetValue(path, out var file) ? file.Contents : throw new FileNotFoundException(path);

    public Stream OpenRead(string path) => new MemoryStream(Encoding.UTF8.GetBytes(ReadAllText(path)));

    public void WriteAllText(string path, string contents) =>
        _files[path] = (contents, new DateTime(Interlocked.Increment(ref _clock), DateTimeKind.Utc));

    public void Move(string sourceFileName, string destFileName, bool overwrite)
    {
        if (!_files.TryRemove(sourceFileName, out var file))
        {
            throw new FileNotFoundException(sourceFileName);
        }

        _files[destFileName] = (file.Contents, new DateTime(Interlocked.Increment(ref _clock), DateTimeKind.Utc));
    }

    public void Delete(string path) => _files.TryRemove(path, out _);

    public void CreateDirectory(string path)
    {
    }

    public bool DirectoryExists(string path) => true;

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) =>
        [.. _files.Keys.Where(p => p.StartsWith(path, StringComparison.OrdinalIgnoreCase))];

    public DateTime GetLastWriteTimeUtc(string path) =>
        _files.TryGetValue(path, out var file) ? file.WrittenAt : DateTime.MinValue;
}
