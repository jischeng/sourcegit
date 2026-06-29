using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using SourceGit.Models;

namespace SourceGit.Remote
{
    /// <summary>
    /// <see cref="IFileSystem"/> implementation that routes file operations to a remote
    /// <c>sourcegit --remote-server</c> via <see cref="RpcClient"/>. The server runs on the
    /// host where the repository lives, so these calls act on the real working tree and
    /// <c>.git</c> internals.
    /// <para>
    /// Text-oriented operations are implemented now. Binary streaming (<see cref="OpenRead"/>/
    /// <see cref="Create"/>) will follow once the protocol gains a chunked transfer; for now
    /// <see cref="OpenRead"/> is text-based (sufficient for source files) and
    /// <see cref="Create"/> throws.
    /// </para>
    /// </summary>
    public sealed class RemoteFileSystem : IFileSystem, IDisposable
    {
        private readonly RpcClient _client;

        public RemoteFileSystem(RpcClient client)
        {
            _client = client;
        }

        public bool FileExists(string path) => (bool)_client.Call("file_exists", new { path });
        public bool DirectoryExists(string path) => (bool)_client.Call("dir_exists", new { path });

        public string ReadAllText(string path) => (string)_client.Call("read_file", new { path })["content"];
        public Task<string> ReadAllTextAsync(string path) => Task.FromResult(ReadAllText(path));

        public void WriteAllText(string path, string content) => _client.Call("write_file", new { path, content });
        public Task WriteAllTextAsync(string path, string content)
        {
            WriteAllText(path, content);
            return Task.CompletedTask;
        }

        public async Task WriteAllLinesAsync(string path, IEnumerable<string> lines)
        {
            await WriteAllTextAsync(path, string.Join("\n", lines)).ConfigureAwait(false);
        }

        public void DeleteFile(string path) => _client.Call("delete_file", new { path });
        public void DeleteDirectory(string path, bool recursive) => _client.Call("delete_dir", new { path, recursive });

        public Stream OpenRead(string path)
        {
            // Text-based for now; binary chunked transfer is a later-phase addition.
            var content = ReadAllText(path);
            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }

        public Stream Create(string path) => throw new NotSupportedException("RemoteFileSystem.Create stream is not supported yet (later phase)");

        public string CreateTempFile(string content) => (string)_client.Call("get_temp_file", new { content })["path"];

        public void Dispose()
        {
            // The client/connection is owned by the caller (shared with RemoteCommandRunner).
        }
    }
}
