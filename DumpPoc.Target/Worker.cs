namespace DumpPoc.Target;

// Simulates a real .NET service: steady memory footprint + periodic CPU bursts.
// This gives both procdump and dotnet-dump something meaningful to capture.
public class Worker(ILogger<Worker> logger) : BackgroundService
{
    // Keep references alive so GC doesn't collect them — realistic heap for dotnet-dump analyze
    private readonly List<byte[]> _heap = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Target worker started. PID={Pid}", Environment.ProcessId);

        // Allocate ~50 MB of heap objects spread across small arrays
        for (var i = 0; i < 50; i++)
            _heap.Add(new byte[1_048_576]); // 1 MB each

        var iteration = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            iteration++;

            // CPU burst every 10 seconds — shows up in procdump as active threads
            if (iteration % 10 == 0)
            {
                DoCpuWork();
                logger.LogInformation("CPU burst #{Iteration} completed", iteration);
            }
            else
            {
                logger.LogInformation("Idle tick #{Iteration}, heap={HeapMb}MB",
                    iteration, _heap.Count);
            }

            await Task.Delay(1_000, stoppingToken);
        }
    }

    private static void DoCpuWork()
    {
        // Tight loop visible in thread stacks — realistic for a CPU spike scenario
        var sum = 0L;
        for (var i = 0; i < 50_000_000; i++)
            sum += i;
        _ = sum;
    }
}
