/*
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Binder;
*/

using sqlserver.tools.queryrecompile.Models;
using sqlserver_autorecompiler;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "SQL Server Recompiler";
    })
    .ConfigureServices((hostContext, services) =>
    {
        List<DatabaseProcOptions>? databaseProcOptions = hostContext.Configuration.GetSection("DatabaseProcOptions").Get<List<DatabaseProcOptions>>();

        services.AddSingleton(databaseProcOptions);
        services.AddHostedService<Worker>();
    })
    .Build();

try
{
    await host.RunAsync();
}
catch { }