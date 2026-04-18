using System.Diagnostics;
using DumpPoc.Shared;
using Microsoft.Extensions.Options;

namespace DumpPoc.Agent;

public class DumpExecutor(IOptions<AgentOptions> opts, ILogger<DumpExecutor> logger)
{
    private const int TimeoutMs = 300_000; // 5 minutes per tool

    public async Task<DumpResult> ExecuteAsync(DumpRequest req)
    {
        var options   = opts.Value;
        var completed = DateTimeOffset.UtcNow.ToString("o");

        var processes = Process.GetProcessesByName(req.ProcessName);
        if (processes.Length == 0)
        {
            var r = Fail(req, $"No process named '{req.ProcessName}' found.", completed);
            return r with { PreviewHtml = EmailTemplate.Render(r) };
        }

        var pid = processes[0].Id;
        Directory.CreateDirectory(options.DumpsDir);
        var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        // ── Full dump via procdump -ma ────────────────────────────────────────
        // Pass the dump directory; procdump names the file itself.
        // We snapshot the dir before/after to identify the created file reliably.
        logger.LogInformation("Full dump: PID {Pid} → {Dir}", pid, options.DumpsDir);

        var beforeFull = Directory.GetFiles(options.DumpsDir, "*.dmp").ToHashSet();
        var (fullExit, fullOut) = await RunAsync(
            options.ProcDumpPath, $"-ma {pid} \"{options.DumpsDir}\" -accepteula");

        string? fullDumpPath      = null;
        long?   fullDumpSizeBytes = null;

        var createdFull = Directory.GetFiles(options.DumpsDir, "*.dmp")
            .FirstOrDefault(f => !beforeFull.Contains(f));

        // procdump exits non-zero even on success — use file creation as the real indicator.
        if (createdFull is not null)
        {
            fullDumpPath      = createdFull;
            fullDumpSizeBytes = new FileInfo(createdFull).Length;
            logger.LogInformation("Full dump done: {Path} ({Size} bytes)", fullDumpPath, fullDumpSizeBytes);
        }
        else
        {
            logger.LogWarning("procdump failed (exit {Code}): {Out}", fullExit, fullOut);
        }

        // ── Managed dump via dotnet-dump (dotnet-modern only) ─────────────────
        string? managedDumpPath      = null;
        long?   managedDumpSizeBytes = null;

        if (req.Runtime == "dotnet-modern")
        {
            var dotnetDump = FindOnPath("dotnet-dump");
            if (dotnetDump is not null)
            {
                var managedPath = Path.Combine(options.DumpsDir, $"{req.RequestId}_{ts}_managed.dmp");
                logger.LogInformation("Managed dump: PID {Pid} → {Path}", pid, managedPath);

                // --type Heap = managed heap + runtime structures only (~50-100 MB vs ~200 MB for Full).
                // Use for dotnet-dump analyze; use Full dump for WinDbg.
                var (mExit, mErr) = await RunAsync(dotnetDump, $"collect -p {pid} -o \"{managedPath}\" --type Heap");

                if (mExit == 0 && File.Exists(managedPath))
                {
                    managedDumpPath      = managedPath;
                    managedDumpSizeBytes = new FileInfo(managedPath).Length;
                    logger.LogInformation("Managed dump done: {Size} bytes", managedDumpSizeBytes);
                }
                else
                {
                    logger.LogWarning("dotnet-dump failed (exit {Code}): {Err}", mExit, mErr);
                }
            }
            else
            {
                logger.LogWarning("dotnet-dump not found on PATH; skipping managed dump");
            }
        }

        // ── Build result ──────────────────────────────────────────────────────
        var bothFailed = fullDumpPath is null && managedDumpPath is null;
        string? error  = bothFailed ? $"Both dump tools failed. procdump output: {fullOut}".Trim() : null;

        var result = new DumpResult(
            req.RequestId, req.ProcessName,
            Success:              !bothFailed,
            FullDumpPath:         fullDumpPath,
            FullDumpSizeBytes:    fullDumpSizeBytes,
            ManagedDumpPath:      managedDumpPath,
            ManagedDumpSizeBytes: managedDumpSizeBytes,
            Error:                error,
            CompletedAt:          completed,
            PreviewHtml:          null);

        return result with { PreviewHtml = EmailTemplate.RenderMarkdown(result) };
    }

    private static string? FindOnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            foreach (var candidate in new[] { Path.Combine(dir, exe + ".exe"), Path.Combine(dir, exe) })
                if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // Returns (exitCode, combined stdout+stderr) — procdump writes to stdout, dotnet-dump to stderr.
    private static async Task<(int exitCode, string output)> RunAsync(string fileName, string arguments)
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
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        var finished = await Task.Run(() => proc.WaitForExit(TimeoutMs));
        if (!finished)
        {
            proc.Kill(entireProcessTree: true);
            return (-1, "Timed out after 5 minutes.");
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        var combined = (await stdoutTask + "\n" + await stderrTask).Trim();
        return (proc.ExitCode, combined);
    }

    private static DumpResult Fail(DumpRequest req, string error, string completedAt) =>
        new(req.RequestId, req.ProcessName,
            Success: false,
            FullDumpPath: null, FullDumpSizeBytes: null,
            ManagedDumpPath: null, ManagedDumpSizeBytes: null,
            Error: error, CompletedAt: completedAt, PreviewHtml: null);
}
