using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Remote
{
    /// <summary>
    /// Client side of the JSON-RPC over stdio protocol.
    /// <para>
    /// A dedicated background read thread consumes the stream, dispatching responses
    /// (matched by id to outstanding <see cref="CallAsync"/> requests) and forwarding
    /// server-pushed notifications (no id) to <see cref="NotificationReceived"/>. This lets
    /// the remote server push events such as working-copy changes back to the client for
    /// auto-refresh.
    /// </para>
    /// <para>
    /// A dedicated thread (not a thread-pool Task) plus
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> avoid the
    /// continuation/thread-pool deadlock that an earlier Task.Run-based version hit.
    /// </para>
    /// </summary>
    public class RpcClient : IDisposable
    {
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly object _writeLock = new();
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode>> _pending = new();
        private readonly Thread _readThread;
        private long _nextId = 0;
        private volatile bool _stopped = false;

        public RpcClient(Stream input, Stream output)
        {
            _reader = new StreamReader(input, Encoding.UTF8);
            _writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true };
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "RpcClient.ReadLoop" };
            _readThread.Start();
        }

        /// <summary>Server-pushed notification (method, params).</summary>
        public event Action<string, JsonNode> NotificationReceived;

        /// <summary>Synchronous request/response. Blocks until the matching response arrives.</summary>
        public JsonNode Call(string method, object parameters) => CallAsync(method, parameters).GetAwaiter().GetResult();

        public async Task<JsonNode> CallAsync(string method, object parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            var req = new Request
            {
                Id = id,
                Method = method,
                Params = JsonSerializer.SerializeToNode(parameters),
            };

            var json = JsonSerializer.Serialize(req);
            lock (_writeLock)
            {
                _writer.WriteLine(json);
                _writer.Flush();
            }

            return await tcs.Task.ConfigureAwait(false);
        }

        private void ReadLoop()
        {
            while (!_stopped)
            {
                string line;
                try
                {
                    line = _reader.ReadLine();
                }
                catch
                {
                    break;
                }

                if (line == null)
                    break;

                JsonNode msg;
                try
                {
                    msg = JsonNode.Parse(line);
                }
                catch
                {
                    continue;
                }

                if (msg == null)
                    continue;

                var idNode = msg["id"];
                if (idNode != null)
                {
                    var id = idNode.GetValue<long>();
                    if (_pending.TryRemove(id, out var tcs))
                    {
                        var err = msg["error"];
                        if (err != null)
                            tcs.TrySetException(new Exception(err["message"]?.GetValue<string>() ?? "remote error"));
                        else
                            tcs.TrySetResult(msg["result"]);
                    }
                }
                else
                {
                    var method = msg["method"]?.GetValue<string>();
                    if (method != null)
                    {
                        try { NotificationReceived?.Invoke(method, msg["params"]); }
                        catch { /* notification handlers must not tear down the read loop */ }
                    }
                }
            }

            foreach (var kv in _pending)
                kv.Value.TrySetException(new Exception("remote server closed the connection"));
        }

        public void Dispose()
        {
            _stopped = true;
            try { _reader.Dispose(); } catch { }
            try { _writer.Dispose(); } catch { }
        }
    }
}
