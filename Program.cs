using App.WindowsService;
using sqlserver.tools.queryrecompile.Models;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "SQL Server Recompiler";
    })
    .ConfigureServices((hostContext, services) =>
    {
        IConfiguration configuration = hostContext.Configuration;
        services.AddSingleton<RecompileService>();
        services.Configure<DatabaseProcOptions>(configuration.GetSection("DatabaseQueryThreshold"));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHostedService<WindowsBackgroundService>();
    })
    .Build();

try
{
    await host.RunAsync();
}
catch { }