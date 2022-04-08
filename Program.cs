/*
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Binder;
*/

using sqlserver.tools.queryrecompile.Models;
using sqlserver_autorecompiler;
using sqlserver_autorecompiler.Models;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "SQL Server Recompiler";
    })
    .ConfigureServices((hostContext, services) =>
    {
        List<DatabaseProcOptions>? databaseProcOptions = hostContext.Configuration.GetSection("DatabaseProcOptions").Get<List<DatabaseProcOptions>>();
        WorkerConfig? cfg = hostContext.Configuration.GetSection(nameof(WorkerConfig)).Get<WorkerConfig>();

        services.AddSingleton(databaseProcOptions);
        services.AddSingleton(cfg);
        services.AddHostedService<Worker>();
    })
    .Build();

try
{
    await host.RunAsync();
}
catch { }