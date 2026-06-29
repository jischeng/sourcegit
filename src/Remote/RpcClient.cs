using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SourceGit.Remote
{
    /// <summary>
    /// Client side of the JSON-RPC over stdio protocol. Sends <see cref="Request"/>s on the
    /// connection's output stream and reads <see cref="Response"/>s back, correlating by id.
    /// <para>
    /// Calls are serialized (one outstanding request at a time) which matches the server's
    /// single-threaded dispatch loop. Server-pushed notifications (for remote change watching)
    /// require a background read loop and are tracked separately.
    /// </para>
    /// </summary>
    public class RpcClient : IDisposable
    {
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly object _lock = new();
        private long _nextId = 1;

        public RpcClient(Stream input, Stream output)
        {
            _reader = new StreamReader(input, Encoding.UTF8);
            _writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true };
        }

        /// <summary>
        /// Send a method call and block until the matching response arrives. Throws on a
        /// remote error or if the connection is closed.
        /// </summary>
        public JsonNode Call(string method, object parameters)
        {
            long id;
            string respLine;

            lock (_lock)
            {
                id = _nextId++;
                var req = new Request
                {
                    Id = id,
                    Method = method,
                    Params = JsonSerializer.SerializeToNode(parameters),
                };

                var json = JsonSerializer.Serialize(req);
                _writer.WriteLine(json);
                respLine = _reader.ReadLine();
            }

            if (respLine == null)
                throw new Exception("remote server closed the connection");

            var resp = JsonSerializer.Deserialize<Response>(respLine);
            if (resp == null)
                throw new Exception("malformed response from remote server");
            if (resp.Id != id)
                throw new Exception($"response id mismatch (sent {id}, got {resp.Id})");
            if (resp.Error != null)
                throw new Exception(resp.Error.Message ?? "remote error");

            return resp.Result;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _writer.Dispose();
        }
    }
}
