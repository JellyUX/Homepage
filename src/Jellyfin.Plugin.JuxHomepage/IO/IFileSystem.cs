namespace Jellyfin.Plugin.JuxHomepage.IO;

/// <summary>
/// Thin abstraction over System.IO file/directory access, so that file-touching code can be tested
/// without hitting the real disk. Introduced in Phase 2 of TODO_V2.md; also used by
/// <c>TransformationPatches.FindLoadSectionsChunks</c> (Phase 9).
/// </summary>
public interface IFileSystem
{
    /// <summary>Determines whether the given file exists.</summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if the file exists.</returns>
    bool FileExists(string path);

    /// <summary>Reads the entire contents of a file as text.</summary>
    /// <param name="path">The file path to read.</param>
    /// <returns>The file contents.</returns>
    string ReadAllText(string path);

    /// <summary>Opens a file for a sequential read, without buffering the whole file in memory.</summary>
    /// <param name="path">The file path to read.</param>
    /// <returns>A read-only stream over the file; the caller disposes it.</returns>
    Stream OpenRead(string path);

    /// <summary>Writes text to a file, creating or overwriting it.</summary>
    /// <param name="path">The file path to write.</param>
    /// <param name="contents">The text to write.</param>
    void WriteAllText(string path, string contents);

    /// <summary>Moves (renames) a file, optionally overwriting the destination.</summary>
    /// <param name="sourceFileName">The source file path.</param>
    /// <param name="destFileName">The destination file path.</param>
    /// <param name="overwrite">Whether to overwrite the destination if it already exists.</param>
    void Move(string sourceFileName, string destFileName, bool overwrite);

    /// <summary>Deletes the given file.</summary>
    /// <param name="path">The file path to delete.</param>
    void Delete(string path);

    /// <summary>Creates the given directory, including any missing parent directories.</summary>
    /// <param name="path">The directory path to create.</param>
    void CreateDirectory(string path);

    /// <summary>Determines whether the given directory exists.</summary>
    /// <param name="path">The directory path to check.</param>
    /// <returns>True if the directory exists.</returns>
    bool DirectoryExists(string path);

    /// <summary>Returns the file paths matching a search pattern under a directory.</summary>
    /// <param name="path">The directory to search.</param>
    /// <param name="searchPattern">The search pattern (e.g. "*.chunk.js").</param>
    /// <param name="searchOption">Whether to search only the top directory or all subdirectories.</param>
    /// <returns>The matching file paths.</returns>
    IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption);

    /// <summary>Returns the UTC timestamp the given file was last written to.</summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>The last-write timestamp, in UTC.</returns>
    DateTime GetLastWriteTimeUtc(string path);
}
