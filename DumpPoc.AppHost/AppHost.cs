var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.DumpPoc_Agent>("dump-agent");
builder.AddProject<Projects.DumpPoc_Target>("dump-target");

// GitHub Actions self-hosted runner — auto-registers with the repo on start.
// Requires GitHub:Pat, GitHub:RepoUrl, and GitHub:Host in appsettings.Development.json (gitignored).
var pat     = builder.Configuration["GitHub:Pat"]     ?? throw new InvalidOperationException("GitHub:Pat not configured");
var repoUrl = builder.Configuration["GitHub:RepoUrl"] ?? throw new InvalidOperationException("GitHub:RepoUrl not configured");

builder.AddContainer("gh-runner", "myoung34/github-runner")
    .WithEnvironment("ACCESS_TOKEN",    pat)
    .WithEnvironment("REPO_URL",        repoUrl)
    .WithEnvironment("RUNNER_NAME",     "aspire-poc-runner")
    .WithEnvironment("LABELS",          "aspire-poc-runner")
    .WithEnvironment("RUNNER_WORKDIR",  "/tmp/runner")
    .WithBindMount("C:\\Dumps\\Poc", "/dumps");

builder.Build().Run();
