using DumpPoc.Shared;

namespace DumpPoc.Agent;

public static class EmailTemplate
{
    // GitHub Actions step summary — markdown rendered natively by GitHub.
    public static string RenderMarkdown(DumpResult r)
    {
        var (icon, heading) = r.Success
            ? (":white_check_mark:", "Dump Completed Successfully")
            : (":x:", "Dump Failed");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## {icon} {heading}");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Request ID | `{r.RequestId}` |");
        sb.AppendLine($"| Process | `{r.ProcessName}` |");
        sb.AppendLine($"| Completed at | {r.CompletedAt} |");

        if (r.FullDumpPath is not null)
        {
            sb.AppendLine($"| Full dump (procdump) | `{r.FullDumpPath}` |");
            sb.AppendLine($"| Full dump size | {r.FullDumpSizeBytes!.Value / 1_048_576.0:F1} MB |");
        }
        else
        {
            sb.AppendLine("| Full dump (procdump) | ⚠️ not captured |");
        }

        if (r.ManagedDumpPath is not null)
        {
            sb.AppendLine($"| Managed dump (dotnet-dump) | `{r.ManagedDumpPath}` |");
            sb.AppendLine($"| Managed dump size | {r.ManagedDumpSizeBytes!.Value / 1_048_576.0:F1} MB |");
        }
        else if (r.Success)
        {
            sb.AppendLine("| Managed dump (dotnet-dump) | ⚠️ not captured |");
        }

        if (r.Error is not null)
        {
            sb.AppendLine();
            sb.AppendLine("### Error");
            sb.AppendLine("```");
            sb.AppendLine(r.Error);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    // Full HTML — for future production email via SendGrid / SMTP.
    public static string Render(DumpResult r)
    {
        var (color, banner) = r.Success
            ? ("#2e7d32", "Dump Completed Successfully")
            : ("#c62828", "Dump Failed");

        var fullRows = r.FullDumpPath is not null
            ? $"""
              <tr><td>Full dump (procdump)</td><td><code>{Escape(r.FullDumpPath)}</code></td></tr>
              <tr><td>Full dump size</td><td>{r.FullDumpSizeBytes!.Value / 1_048_576.0:F1} MB</td></tr>
              """
            : "<tr><td>Full dump</td><td><em>not captured</em></td></tr>";

        var managedRows = r.ManagedDumpPath is not null
            ? $"""
              <tr><td>Managed dump (dotnet-dump)</td><td><code>{Escape(r.ManagedDumpPath)}</code></td></tr>
              <tr><td>Managed dump size</td><td>{r.ManagedDumpSizeBytes!.Value / 1_048_576.0:F1} MB</td></tr>
              """
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
              body { font-family: Arial, sans-serif; max-width: 760px; margin: 40px auto; color: #212121; }
              .banner { background: {{color}}; color: #fff; padding: 18px 24px; border-radius: 6px; }
              table { border-collapse: collapse; width: 100%; margin-top: 20px; }
              td { padding: 8px 12px; border-bottom: 1px solid #e0e0e0; }
              td:first-child { font-weight: bold; width: 200px; color: #555; }
              code { background: #f5f5f5; padding: 2px 6px; border-radius: 3px; font-size: 0.9em; }
            </style>
            </head>
            <body>
              <div class="banner"><h2 style="margin:0">{{banner}}</h2></div>
              <table>
                <tr><td>Request ID</td><td>{{Escape(r.RequestId)}}</td></tr>
                <tr><td>Process</td><td>{{Escape(r.ProcessName)}}</td></tr>
                <tr><td>Completed at</td><td>{{Escape(r.CompletedAt)}}</td></tr>
                {{fullRows}}
                {{managedRows}}
              </table>
              {{errorSection}}
            </body>
            </html>
            """;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
