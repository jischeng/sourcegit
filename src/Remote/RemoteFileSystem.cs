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
    /// Text operations use the <c>read_file</c>/<c>write_file</c> RPCs; <see cref="OpenRead"/>
    /// uses <c>read_file_base64</c> and spills to a local temp file so binary files (images,
    /// blobs) are handled correctly. <see cref="Create"/> is still not supported remotely.
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
            // Fetch the file as base64 so binary files (images, etc.) are not corrupted, then
            // spill to a local temp file and return a stream that deletes it on close. The
            // whole file is buffered in memory during transfer, which is fine for the image
            // previews / blob views that are the typical binary use case here.
            var b64 = (string)_client.Call("read_file_base64", new { path })["content"];
            var bytes = Convert.FromBase64String(b64);
            var tmp = Path.GetTempFileName();
            File.WriteAllBytes(tmp, bytes);
            return new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
        }

        public Stream Create(string path) => throw new NotSupportedException("RemoteFileSystem.Create stream is not supported yet (later phase)");

        public string CreateTempFile(string content) => (string)_client.Call("get_temp_file", new { content })["path"];

        public void Dispose()
        {
            // The client/connection is owned by the caller (shared with RemoteCommandRunner).
        }
    }
}
