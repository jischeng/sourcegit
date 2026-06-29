using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public static class QueryFileContent
    {
        public static async Task<Stream> RunAsync(string repo, string revision, string file)
        {
            var spec = new Command.RunSpec
            {
                Args = $"show {revision}:{file.Quoted()}",
                WorkingDirectory = repo,
            };

            var stream = new MemoryStream();
            try
            {
                using var proc = LocalCommandRunner.Instance.Start(spec);
                await proc.StdoutStream.CopyToAsync(stream).ConfigureAwait(false);
                await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Models.Notification.Send(repo, $"Failed to query file content: {e}", true);
            }

            stream.Position = 0;
            return stream;
        }

        public static async Task<Stream> FromLFSAsync(string repo, string oid, long size)
        {
            var spec = new Command.RunSpec
            {
                Args = "lfs smudge",
                WorkingDirectory = repo,
                RedirectStandardInput = true,
            };

            var stream = new MemoryStream();
            try
            {
                using var proc = LocalCommandRunner.Instance.Start(spec);
                await proc.Stdin.WriteLineAsync("version https://git-lfs.github.com/spec/v1").ConfigureAwait(false);
                await proc.Stdin.WriteLineAsync($"oid sha256:{oid}").ConfigureAwait(false);
                await proc.Stdin.WriteLineAsync($"size {size}").ConfigureAwait(false);
                await proc.StdoutStream.CopyToAsync(stream).ConfigureAwait(false);
                await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Models.Notification.Send(repo, $"Failed to query file content: {e}", true);
            }

            stream.Position = 0;
            return stream;
        }
    }
}
