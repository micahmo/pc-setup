using System.Diagnostics;
using System.IO;
using System.Text;

namespace PCSetup.Services;

public sealed record ProcessResult(int ExitCode, string Output);

public static class ProcessRunner
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "PCSetup");

    // Every script gets the same preamble: stop on errors, no progress bars, UTF-8 output.
    private const string Preamble = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [Text.Encoding]::UTF8

        """;

    /// <summary>Runs a detect script. True when it returns a truthy last value; errors count as "not done".</summary>
    public static async Task<bool> DetectAsync(string script, CancellationToken ct = default)
    {
        var wrapped = $$"""
            try { $r = & {
            {{script}}
            } } catch { exit 1 }
            if ([bool]($r | Select-Object -Last 1)) { exit 0 } else { exit 1 }
            """;
        var result = await PowerShellAsync(wrapped, null, ct);
        return result.ExitCode == 0;
    }

    public static async Task<ProcessResult> PowerShellAsync(string script, Action<string>? onLine, CancellationToken ct = default)
    {
        Directory.CreateDirectory(TempDir);
        var file = Path.Combine(TempDir, $"{Guid.NewGuid():N}.ps1");
        // BOM so Windows PowerShell 5.1 reads the file as UTF-8.
        File.WriteAllText(file, Preamble + script, new UTF8Encoding(true));
        try
        {
            // NonInteractive makes a script that tries to prompt fail instead of hanging in a hidden window.
            return await RunAsync("powershell.exe", $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{file}\"", onLine, ct);
        }
        finally
        {
            TryDelete(file);
        }
    }

    public static async Task<ProcessResult> RunAsync(string exe, string args, Action<string>? onLine, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        void Collect(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (output) output.AppendLine(line);
            onLine?.Invoke(line);
        }
        process.OutputDataReceived += (_, e) => Collect(e.Data);
        process.ErrorDataReceived += (_, e) => Collect(e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, $"Couldn't start {exe}: {ex.Message}");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return new ProcessResult(process.ExitCode, output.ToString());
    }

    /// <summary>Opens a URL, ms-settings: link, or program the way Explorer would.</summary>
    public static void Open(string target)
    {
        var expanded = Environment.ExpandEnvironmentVariables(target);
        var isLink = Uri.TryCreate(expanded, UriKind.Absolute, out var uri) && !uri.IsFile;
        if (isLink || Directory.Exists(expanded))
        {
            // Hand links and folders to the already-running Explorer. Opening them straight from this
            // elevated process fails for folders ("Location is not available") and starts browsers as admin.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{expanded}\"") { UseShellExecute = false });
            return;
        }
        Process.Start(new ProcessStartInfo(expanded) { UseShellExecute = true });
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
