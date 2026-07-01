using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    /// <see cref="IFileSystem"/> implementation that delegates directly to <see cref="File"/>/
    /// <see cref="Directory"/>. This is the local-repository behavior, preserved exactly when
    /// the filesystem abstraction is used in place of raw File./Directory. calls.
    /// </summary>
    public sealed class LocalFileSystem : IFileSystem
    {
        public static readonly LocalFileSystem Instance = new();

        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path);
        public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);

        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
        public Task WriteAllTextAsync(string path, string content) => File.WriteAllTextAsync(path, content);
        public Task WriteAllLinesAsync(string path, IEnumerable<string> lines) => File.WriteAllLinesAsync(path, lines);

        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

        public Stream OpenRead(string path) => File.OpenRead(path);
        public Stream Create(string path) => File.Create(path);

        public string CreateTempFile(string content)
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, content);
            return path;
        }
    }
}
