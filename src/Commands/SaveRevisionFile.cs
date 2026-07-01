using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public static class SaveRevisionFile
    {
        public static async Task RunAsync(string repo, string revision, string file, string saveTo)
        {
            var dir = Path.GetDirectoryName(saveTo) ?? string.Empty;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var isLFSFiltered = await new IsLFSFiltered(repo, revision, file).GetResultAsync().ConfigureAwait(false);
            if (isLFSFiltered)
            {
                var pointerStream = await QueryFileContent.RunAsync(repo, revision, file).ConfigureAwait(false);
                await ExecCmdAsync(repo, "lfs smudge", saveTo, pointerStream).ConfigureAwait(false);
            }
            else
            {
                await ExecCmdAsync(repo, $"show {revision}:{file.Quoted()}", saveTo).ConfigureAwait(false);
            }
        }

        private static async Task ExecCmdAsync(string repo, string args, string outputFile, Stream input = null)
        {
            var spec = new Command.RunSpec
            {
                Args = args,
                WorkingDirectory = repo,
                RedirectStandardInput = true,
            };

            await using (var sw = File.Create(outputFile))
            {
                try
                {
                    using var proc = (CommandRunnerRegistry.Get(repo) ?? LocalCommandRunner.Instance).Start(spec);

                    if (input != null)
                    {
                        var inputString = await new StreamReader(input).ReadToEndAsync().ConfigureAwait(false);
                        await proc.Stdin.WriteAsync(inputString).ConfigureAwait(false);
                    }

                    await proc.StdoutStream.CopyToAsync(sw).ConfigureAwait(false);
                    await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Models.Notification.Send(repo, "Save file failed: " + e.Message, true);
                }
            }
        }
    }
}
