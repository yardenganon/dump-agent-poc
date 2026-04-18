using System.Diagnostics;
using DumpPoc.Shared;
using Microsoft.Extensions.Options;

namespace DumpPoc.Agent;

public class DumpExecutor(IOptions<AgentOptions> opts, ILogger<DumpExecutor> logger)
{
    private const int TimeoutMs = 300_000; // 5 minutes

    public async Task<DumpResult> ExecuteAsync(DumpRequest req)
    {
        var options = opts.Value;
        var completedAt = DateTimeOffset.UtcNow.ToString("o");

        var processes = Process.GetProcessesByName(req.ProcessName);
        if (processes.Length == 0)
        {
            var result = Fail(req, $"No process named '{req.ProcessName}' found.", completedAt);
            return result with { PreviewHtml = EmailTemplate.Render(result) };
        }

        var pid = processes[0].Id;
        Directory.CreateDirectory(options.DumpsDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var dumpPath  = Path.Combine(options.DumpsDir, $"{req.RequestId}_{timestamp}.dmp");

        var (fileName, arguments) = SelectTool(req.Runtime, options.ProcDumpPath, pid, dumpPath);

        logger.LogInformation("Dumping PID {Pid} via {Tool} → {Path}", pid, fileName, dumpPath);

        var (exitCode, stderr) = await RunProcessAsync(fileName, arguments);

        if (exitCode != 0 || !File.Exists(dumpPath))
        {
            var result = Fail(req, $"Dump tool exited with code {exitCode}. {stderr}".Trim(), completedAt);
            return result with { PreviewHtml = EmailTemplate.Render(result) };
        }

        var size = new FileInfo(dumpPath).Length;
        var success = new DumpResult(
            req.RequestId, req.ProcessName,
            Success: true, DumpPath: dumpPath, DumpSizeBytes: size,
            Error: null, CompletedAt: completedAt, PreviewHtml: null);

        return success with { PreviewHtml = EmailTemplate.Render(success) };
    }

    private static (string fileName, string arguments) SelectTool(
        string runtime, string procDumpPath, int pid, string dumpPath)
    {
        if (runtime == "dotnet-modern")
        {
            var dotnetDump = FindOnPath("dotnet-dump");
            if (dotnetDump is not null)
                return (dotnetDump, $"collect -p {pid} -o \"{dumpPath}\"");
        }

        // fallback: procdump full dump
        var dir = Path.GetDirectoryName(dumpPath)!;
        return (procDumpPath, $"-ma {pid} \"{dir}\" -accepteula");
    }

    private static string? FindOnPath(string exe)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                    ?? [];
        foreach (var dir in paths)
        {
            var candidate = Path.Combine(dir, exe + ".exe");
            if (File.Exists(candidate)) return candidate;

            candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task<(int exitCode, string stderr)> RunProcessAsync(
        string fileName, string arguments)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        proc.Start();

        var stderrTask = proc.StandardError.ReadToEndAsync();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();

        var completed = await Task.Run(() => proc.WaitForExit(TimeoutMs));
        if (!completed)
        {
            proc.Kill(entireProcessTree: true);
            return (-1, "Dump tool timed out after 5 minutes.");
        }

        await Task.WhenAll(stderrTask, stdoutTask);
        return (proc.ExitCode, await stderrTask);
    }

    private static DumpResult Fail(DumpRequest req, string error, string completedAt) =>
        new(req.RequestId, req.ProcessName,
            Success: false, DumpPath: null, DumpSizeBytes: null,
            Error: error, CompletedAt: completedAt, PreviewHtml: null);
}
