namespace App.WindowsService;

public sealed class WindowsBackgroundService : BackgroundService
{
    private readonly RecompileService _recompileService;
    private readonly ILogger<WindowsBackgroundService> _logger;

    public WindowsBackgroundService(RecompileService recompileService, ILogger<WindowsBackgroundService> logger)
    {
        (_recompileService, _logger) = (recompileService, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting");
        while (!stoppingToken.IsCancellationRequested)
        {
            await _recompileService.ProcessXelStream(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        _logger.LogInformation("Exiting.");
    }
}