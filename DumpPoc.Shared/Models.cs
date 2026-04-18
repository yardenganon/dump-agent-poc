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
    string? DumpPath,
    long?   DumpSizeBytes,
    string? Error,
    string  CompletedAt,
    string? PreviewHtml  // POC-only: written to $GITHUB_STEP_SUMMARY; omitted in prod
);
