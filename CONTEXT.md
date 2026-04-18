# Dump-on-Demand — Project Context
> Drop this file in `C:\Users\Yarden\RiderProjects\DumpAgentProject\` so Claude Code picks it up automatically.

---

## What This Is

An internal developer tool that lets engineers trigger a full Windows process dump
on demand via GitHub Actions, with lead approval, and receive the dump file via
email (signed GCS URL). Designed to replace manual WinDbg sessions for diagnosing
CPU spikes and other production incidents.

---

## Current Status

**POC phase** — everything runs locally on Windows 11.
No GCS, no Cloud Function, no email sending yet.
The agent returns a rendered HTML email preview as the HTTP response body.

---

## Tech Stack

| Layer | Choice | Reason |
|---|---|---|
| Language | C# .NET 10 | Team knows it; all services already use it |
| Local orchestration | Aspire 13.2 | Dashboard, telemetry, service discovery out of the box |
| Dump tool (.NET 5+) | `dotnet-dump collect` | Managed heap, smaller file |
| Dump tool (all runtimes) | `procdump.exe -ma` | Full Windows dump, works for .NET Framework + native |
| Storage (production) | GCS bucket | Private, lifecycle auto-delete 14d, signed URLs |
| Notification (production) | GCP Cloud Function (.NET 10) | Same language, Workload Identity |
| Trigger | GitHub Actions `workflow_dispatch` | Dropdown inputs, built-in approval via GH Environments |
| Approval gate | GH Environment `dump-production` | Required reviewers, no custom infra |

---

## Project Structure

```
C:\Users\Yarden\RiderProjects\DumpAgentProject\
  DumpPoc.sln
  CONTEXT.md                        ← this file
  bootstrap.ps1                     ← one-shot setup script
  │
  DumpPoc.AppHost\                  ← Aspire orchestrator (run this to start everything)
    Program.cs
    DumpPoc.AppHost.csproj
    appsettings.json
  │
  DumpPoc.Agent\                    ← The HTTP dump agent (ASP.NET Core minimal API)
    Program.cs                      ← endpoints: GET /health, POST /dump
    DumpExecutor.cs                 ← finds process, runs dump tool, returns DumpResult
    EmailTemplate.cs                ← renders HTML email preview (success + failure)
    appsettings.json                ← Agent:Secret, Agent:DumpsDir, Agent:ProcDumpPath
    appsettings.Development.json
    DumpPoc.Agent.csproj
  │
  DumpPoc.Shared\                   ← Shared models (referenced by Agent + future Cloud Function)
    Models.cs                       ← DumpRequest record, DumpResult record
    DumpPoc.Shared.csproj
```

---

## Key Models

```csharp
// DumpPoc.Shared/Models.cs

public record DumpRequest(
    string RequestId,
    string ProcessName,
    string Runtime,        // "dotnet-modern" | "dotnet-framework" | "native"
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
    string  CompletedAt
);
```

---

## Agent Endpoints

### `GET /health`
Returns `{ status, hostname, timestamp }` — used by Aspire dashboard + deploy script smoke test.

### `POST /dump`
**Headers:** `X-Dump-Secret: poc-secret`
**Body:**
```json
{
  "requestId":      "test-001",
  "processName":    "notepad",
  "runtime":        "native",
  "requesterEmail": "you@company.com",
  "requesterName":  "Yarden"
}
```
**Response:** `text/html` — rendered email preview (open in browser)

---

## Dump Tool Selection Logic

```
runtime == "dotnet-modern" AND dotnet-dump.exe exists
    → dotnet-dump collect -p <pid> -o <path>

everything else (dotnet-framework, native, fallback)
    → procdump.exe -ma <pid> <path> -accepteula
```

Both tools must be pre-installed:
- `procdump.exe` → `C:\Tools\procdump.exe`
- `dotnet-dump` → installed as .NET global tool: `dotnet tool install --global dotnet-dump`

---

## Configuration (`appsettings.json`)

```json
{
  "Agent": {
    "Secret":       "poc-secret",
    "DumpsDir":     "C:\\Dumps\\Poc",
    "ProcDumpPath": "C:\\Tools\\procdump.exe"
  }
}
```

---

## How to Run

```powershell
# 1. Bootstrap (first time only — creates files, installs tools, restores packages)
cd C:\Users\Yarden\RiderProjects\DumpAgentProject
powershell -ExecutionPolicy Bypass -File bootstrap.ps1

# 2. Run via Aspire
cd DumpPoc.AppHost
dotnet run

# 3. Aspire dashboard opens at https://localhost:15888

# 4. Test in a second terminal (make sure notepad.exe is running first)
Invoke-RestMethod `
  -Uri http://localhost:5100/dump `
  -Method POST `
  -ContentType "application/json" `
  -Headers @{ "X-Dump-Secret" = "poc-secret" } `
  -Body (ConvertTo-Json @{
      requestId      = "test-001"
      processName    = "notepad"
      runtime        = "native"
      requesterEmail = "you@company.com"
      requesterName  = "Yarden"
  })
```

---

## Production Architecture (post-POC)

### VM Path (Windows VMs)
```
GH Actions workflow_dispatch
  → validate job (input checks, generate RequestId)
  → await-approval job (GH Environment: dump-production — lead clicks Approve)
  → dump-vm job
      → POST http://{instance}.internal:5100/dump  (X-Dump-Secret header)
      → Agent (C# .NET 10 Windows Service) receives request
      → Runs procdump / dotnet-dump
      → Uploads .dmp to GCS (Workload Identity)
      → POSTs completion webhook to Cloud Function
          → Cloud Function generates 48h signed URL
          → Sends email to requester
```

### Container Path (GKE Windows nodes)
```
GH Actions (same validate + approval)
  → dump-container job
      → kubectl exec <pod> -- powershell -Command "procdump / dotnet-dump"
      → kubectl cp dump out of pod
      → gsutil cp to GCS
      → POST completion webhook to Cloud Function
          → same notification flow as VM path
```

---

## GCS Bucket Layout (production)

```
gs://company-dumps/
  windows-vm/        {service}/{date}/{requestId}_{timestamp}.dmp
  windows-container/ {service}/{date}/{requestId}_{timestamp}.dmp
  registry/          instances/{hostname}.json   ← service registry
```

Bucket config: private, no public access, lifecycle auto-delete 14d, audit logging ON.

---

## Production Cloud Function (C# .NET 10)

Two entrypoints in `DumpPoc.Notifier`:
- `GcsTriggerFunction` — triggered by GCS object finalize event (VM path)
- `WebhookFunction`    — HTTP trigger called by GH Actions container job

Both call `NotificationHandler` which generates a signed URL and sends email via SendGrid.
Auth: `X-Webhook-Secret` header checked against GCP Secret Manager.

---

## GitHub Actions Secrets Required (production)

| Secret | Purpose |
|---|---|
| `DUMP_AGENT_SECRET` | Shared secret for VM agent HTTP auth |
| `DUMP_COMPLETION_WEBHOOK_URL` | Cloud Function URL |
| `DUMP_BUCKET_NAME` | GCS bucket name |
| `GCP_SA_KEY` | Service account JSON |
| `GKE_CLUSTER_NAME` | GKE cluster |
| `GKE_REGION` | e.g. `us-central1` |
| `GCP_PROJECT_ID` | GCP project |

---

## Decisions Made

| Topic | Decision |
|---|---|
| VM agent language | C# .NET 10 Worker Service (self-contained .exe) |
| VM→agent transport | HTTP (not Pub/Sub) — simpler, VMs have stable hostnames |
| Container→dump transport | `kubectl exec` directly from GH Actions — no agent needed |
| Dump strategy | Always take full Windows dump (procdump -ma); optionally layer dotnet-dump for .NET 5+ |
| Full vs managed dump | Full dump is superset — use it for WinDbg/mcp-windbg; managed dump for quick dotnet-dump analyze |
| Cloud Function language | C# .NET 10 (same stack as agent, share models via DumpPoc.Shared) |
| GCS auth | Workload Identity — no credentials on disk |
| Email provider | SendGrid (or internal SMTP) |
| Approval mechanism | GH Environment protection rules — zero custom infra |
| Agent deployment | PowerShell script (`deploy-agent.ps1`) baked into VM image via GCP startup script |
| Linux support | Deferred to Phase 3 |
| mcp-windbg AI analysis | Deferred to Phase 2 |

---

## Phase Roadmap

| Phase | Scope |
|---|---|
| **1 (now)** | POC — Windows 11 local, Aspire, HTML preview |
| **2** | Windows VMs in GCP — HTTP agent, GCS upload, Cloud Function email |
| **3** | GKE Windows containers — kubectl exec path |
| **4** | mcp-windbg AI pre-analysis appended to email |
| **5** | Linux VMs + containers |
| **6** | Internal web UI or Slack trigger |

---

## IDE

JetBrains Rider (primary) + VS Code. OS: Windows 11. .NET 10 SDK installed.
