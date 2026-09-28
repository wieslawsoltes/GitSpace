using System.Diagnostics;
using System.Text;

namespace GitSpace.Git;

public sealed record GitProcessResult(int ExitCode, string Output, string Error);
public sealed class GitCommandException(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
/// <summary>Uses ArgumentList, never a shell. Hooks are disabled; installed Git owns credential handling.</summary>
public sealed class GitProcess : IDisposable
{
    private readonly string _hooks = Path.Combine(Path.GetTempPath(), "gitspace-hooks-" + Guid.NewGuid().ToString("N"));
    public GitProcess() => Directory.CreateDirectory(_hooks);
    public async Task<GitProcessResult> RunAsync(string directory, IEnumerable<string> arguments, CancellationToken cancellation = default, string? input = null, bool allowFailure = false, IReadOnlyDictionary<string, string>? environment = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false)
        };
        var argv = arguments.ToArray();
        // Do not set literal path mode for compound commands such as stash: Git's own
        // internal pathspecs include magic prefixes, and globally disabling those leaves
        // untracked files behind. Only explicitly supplied file lists need literal mode.
        info.ArgumentList.Add("--no-pager");
        if (argv.Contains("--") || argv.Contains("--pathspec-file-nul")) info.ArgumentList.Add("--literal-pathspecs");
        foreach (var arg in new[] { "-c", "color.ui=false", "-c", "core.quotepath=false", "-c", "core.hooksPath=" + _hooks }) info.ArgumentList.Add(arg);
        foreach (var arg in argv) info.ArgumentList.Add(arg);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_EDITOR"] = "true";
        info.Environment["GIT_SEQUENCE_EDITOR"] = "true";
        if (environment is not null) foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        using var process = new Process { StartInfo = info };
        try { if (!process.Start()) throw new InvalidOperationException("Git did not start."); }
        catch (System.ComponentModel.Win32Exception e) { throw new InvalidOperationException("Install Git 2.30 or newer and make it available on PATH.", e); }
        void Kill() { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
        using var registration = timeout.Token.Register(Kill);
        async Task<string> Read(Stream stream, Encoding encoding)
        {
            using var result = new MemoryStream(); var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    var count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    if (result.Length + count > 16 * 1024 * 1024) throw new InvalidDataException("Git output exceeds the 16 MiB safety limit.");
                    result.Write(buffer, 0, count);
                }
                // Do not let StreamReader strip a file's UTF-8 BOM or guess an encoding.
                return encoding.GetString(result.GetBuffer(), 0, checked((int)result.Length));
            }
            catch { Kill(); throw; }
        }
        var stdout = Read(process.StandardOutput.BaseStream, new UTF8Encoding(false, true));
        var stderr = Read(process.StandardError.BaseStream, Encoding.UTF8);
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false); var error = await stderr.ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            var result = new GitProcessResult(process.ExitCode, output, error);
            if (!allowFailure && result.ExitCode != 0) throw new GitCommandException(string.IsNullOrWhiteSpace(error) ? "Git exited with code " + result.ExitCode : error.Trim(), result.ExitCode);
            return result;
        }
        catch
        {
            Kill();
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch { }
            throw;
        }
    }
    public void Dispose() { try { Directory.Delete(_hooks); } catch (IOException) { } }
}
