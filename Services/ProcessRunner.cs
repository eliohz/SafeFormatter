using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SafeFormatter.Services
{
    public static class ProcessRunner
    {
        public static async Task<(int exitCode, string output)> RunAsync(string fileName, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var sb = new StringBuilder();
            var tcs = new TaskCompletionSource<int>();

            p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var code = await tcs.Task.ConfigureAwait(false);
            return (code, sb.ToString());
        }
    }
}