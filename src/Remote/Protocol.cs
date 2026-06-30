using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SourceGit.Remote
{
    /// <summary>
    /// Wire format for the sourcegit remote server protocol.
    /// <para>
    /// The client launches the server with <c>ssh &lt;host&gt; sourcegit --remote-server</c>;
    /// the SSH channel then carries one JSON-RPC message per line on stdin/stdout.
    /// Requests carry an <c>id</c>; the server replies with a <see cref="Response"/>
    /// bearing the same id. The server may also push <c>Notification</c>s (no id) for
    /// events such as working-copy changes (added in a later phase).
    /// </para>
    /// </summary>
    public class Request
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("method")] public string Method { get; set; }
        [JsonPropertyName("params")] public JsonNode Params { get; set; }
    }

    public class RpcError
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
    }

    public class Response
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("result")] public JsonNode Result { get; set; }
        [JsonPropertyName("error")] public RpcError Error { get; set; }
    }

    public class Notification
    {
        [JsonPropertyName("method")] public string Method { get; set; }
        [JsonPropertyName("params")] public JsonNode Params { get; set; }
    }

    /// <summary>Parameters for the <c>exec_git</c> method.</summary>
    public class ExecGitParams
    {
        [JsonPropertyName("args")] public string Args { get; set; }
        [JsonPropertyName("working_dir")] public string WorkingDir { get; set; }
        [JsonPropertyName("ssh_key")] public string SshKey { get; set; }
        [JsonPropertyName("editor")] public string Editor { get; set; }
        [JsonPropertyName("stdin")] public string Stdin { get; set; }
    }

    /// <summary>Result of the <c>exec_git</c> method.</summary>
    public class ExecGitResult
    {
        [JsonPropertyName("stdout")] public string Stdout { get; set; }
        [JsonPropertyName("stderr")] public string Stderr { get; set; }
        [JsonPropertyName("exit_code")] public int ExitCode { get; set; }
    }

    /// <summary>One entry returned by the <c>list_dir</c> method.</summary>
    public class ListDirEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("is_dir")] public bool IsDir { get; set; }
    }

    /// <summary>Result of the <c>list_dir</c> method.</summary>
    public class ListDirResult
    {
        [JsonPropertyName("path")] public string Path { get; set; }
        [JsonPropertyName("entries")] public List<ListDirEntry> Entries { get; set; } = new();
    }
}
