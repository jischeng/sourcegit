using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    /// Abstracts filesystem access to the repository working tree and the <c>.git</c>
    /// directory.
    /// <para>
    /// The local implementation delegates directly to <see cref="File"/>/<see cref="Directory"/>
    /// and keeps existing behavior unchanged. A future remote implementation will route
    /// these calls to a lightweight server over an SSH channel, so that operations on a
    /// repository living on a remote host do not require the files to exist locally.
    /// </para>
    /// <para>
    /// Only paths that refer to the managed repository (working tree or <c>.git</c>
    /// internals) need to go through this interface. Application data living under
    /// <c>Native.OS.DataDir</c> (preferences, avatar cache, etc.) stays local on both
    /// sides and continues to use <see cref="File"/>/<see cref="Directory"/> directly.
    /// </para>
    /// </summary>
    public interface IFileSystem
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);

        string ReadAllText(string path);
        Task<string> ReadAllTextAsync(string path);

        void WriteAllText(string path, string content);
        Task WriteAllTextAsync(string path, string content);
        Task WriteAllLinesAsync(string path, IEnumerable<string> lines);

        void DeleteFile(string path);
        void DeleteDirectory(string path, bool recursive);

        Stream OpenRead(string path);
        Stream Create(string path);

        /// <summary>
        /// Create a temporary file reachable by the git process and write the given
        /// content into it, returning the path. Locally this is <c>Path.GetTempFileName</c>
        /// followed by a write; remotely the file is created on the server hosting git.
        /// Used for pathspec/commit-message/patch data that git must read from disk.
        /// </summary>
        string CreateTempFile(string content);
    }
}
