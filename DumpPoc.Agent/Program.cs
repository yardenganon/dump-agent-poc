using DumpPoc.Agent;
using DumpPoc.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddSingleton<DumpExecutor>();

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/health", () => Results.Ok(new
{
    status    = "ok",
    hostname  = Environment.MachineName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapPost("/dump", async (HttpRequest req, DumpExecutor executor,
    Microsoft.Extensions.Options.IOptions<AgentOptions> opts) =>
{
    if (!req.Headers.TryGetValue("X-Dump-Secret", out var secret) ||
        secret != opts.Value.Secret)
        return Results.Unauthorized();

    var dumpReq = await req.ReadFromJsonAsync<DumpRequest>();
    if (dumpReq is null)
        return Results.BadRequest("Invalid request body.");

    var result = await executor.ExecuteAsync(dumpReq);
    return Results.Ok(result);
});

app.Run();
