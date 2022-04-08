namespace App.WindowsService;

public sealed class WindowsBackgroundService : BackgroundService
{
    private readonly ILogger<WindowsBackgroundService> _logger;

    public WindowsBackgroundService(ILogger<WindowsBackgroundService> logger)
    {
        (_logger) = (logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting");
        while (!stoppingToken.IsCancellationRequested)
        {
            List<Task>? workers = new();
            foreach (int delay in new List<int>() { 1000, 2000, 3000 })
            {
                workers.Add(DoRealWork(delay, stoppingToken));
            }

            await Task.WhenAll(workers.ToArray());
        }
        _logger.LogInformation("Exiting.");
    }
    private async Task DoRealWork(int delay, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("worker {delay} checking in at {time}", delay, DateTimeOffset.Now);
            await Task.Delay(delay, stoppingToken);
        }
    }
}