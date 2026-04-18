using DumpPoc.Shared;

namespace DumpPoc.Agent;

public static class EmailTemplate
{
    public static string Render(DumpResult r)
    {
        var (color, banner) = r.Success
            ? ("#2e7d32", "Dump Completed Successfully")
            : ("#c62828", "Dump Failed");

        var sizeRow = r.DumpSizeBytes.HasValue
            ? $"<tr><td>Size</td><td>{r.DumpSizeBytes.Value / 1_048_576.0:F1} MB</td></tr>"
            : string.Empty;

        var pathRow = r.DumpPath is not null
            ? $"<tr><td>Dump path</td><td><code>{r.DumpPath}</code></td></tr>"
            : string.Empty;

        var errorSection = r.Error is not null
            ? $"""
              <h3 style="color:#c62828">Error</h3>
              <pre style="background:#fff3f3;padding:12px;border-radius:4px">{Escape(r.Error)}</pre>
              """
            : string.Empty;

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><title>Dump Report — {{Escape(r.RequestId)}}</title>
            <style>
              body { font-family: Arial, sans-serif; max-width: 700px; margin: 40px auto; color: #212121; }
              .banner { background: {{color}}; color: #fff; padding: 18px 24px; border-radius: 6px; }
              table { border-collapse: collapse; width: 100%; margin-top: 20px; }
              td { padding: 8px 12px; border-bottom: 1px solid #e0e0e0; }
              td:first-child { font-weight: bold; width: 160px; color: #555; }
              code { background: #f5f5f5; padding: 2px 6px; border-radius: 3px; }
            </style>
            </head>
            <body>
              <div class="banner"><h2 style="margin:0">{{banner}}</h2></div>
              <table>
                <tr><td>Request ID</td><td>{{Escape(r.RequestId)}}</td></tr>
                <tr><td>Process</td><td>{{Escape(r.ProcessName)}}</td></tr>
                <tr><td>Completed at</td><td>{{Escape(r.CompletedAt)}}</td></tr>
                {{pathRow}}
                {{sizeRow}}
              </table>
              {{errorSection}}
            </body>
            </html>
            """;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
