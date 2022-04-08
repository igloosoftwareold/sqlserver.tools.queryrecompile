using App.WindowsService;
using sqlserver.tools.queryrecompile.Models;

namespace sqlserver_autorecompiler
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<WindowsBackgroundService> _logger;
        private readonly List<DatabaseProcOptions> _databaseProcOptions;

        public Worker(ILogger<WindowsBackgroundService> logger, List<DatabaseProcOptions> databaseProcOptions)
        {
            _logger = logger;
            _databaseProcOptions = databaseProcOptions;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                List<Task>? workers = new();
                foreach (DatabaseProcOptions? databaseProcOption in _databaseProcOptions)
                {
                    workers.Add(Task.Run(() => DoRealWork(databaseProcOption, stoppingToken), stoppingToken));
                }
                await Task.WhenAll(workers.ToArray());
            }
        }
        private async Task DoRealWork(DatabaseProcOptions databaseProcOption, CancellationToken stoppingToken)
        {
            RecompileService? s = new(_logger, databaseProcOption);
            await s.ProcessXelStream(stoppingToken);
        }
    }
}
