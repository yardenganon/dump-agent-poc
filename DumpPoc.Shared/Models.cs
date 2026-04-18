namespace DumpPoc.Shared;

public record DumpRequest(
    string RequestId,
    string ProcessName,
    string Runtime,
    string RequesterEmail,
    string RequesterName
);

public record DumpResult(
    string  RequestId,
    string  ProcessName,
    bool    Success,
    string? FullDumpPath,           // procdump -ma (always attempted)
    long?   FullDumpSizeBytes,
    string? ManagedDumpPath,        // dotnet-dump collect (dotnet-modern only)
    long?   ManagedDumpSizeBytes,
    string? Error,
    string  CompletedAt,
    string? PreviewHtml             // POC-only: written to $GITHUB_STEP_SUMMARY
);
