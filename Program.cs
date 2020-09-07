using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using sqlserver.tools.queryrecompile.Models;

namespace sqlserver.tools.queryrecompile
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureLogging(loggerFactory => loggerFactory.AddEventLog())
            .ConfigureServices((hostContext, services) =>
            {
                IConfiguration configuration = hostContext.Configuration;
                services.AddOptions();
                services.Configure<DatabaseProcOptions>(configuration.GetSection("DatabaseQueryThreshold"));
                services.AddSingleton<IConfiguration>(configuration);
                services.AddHostedService<Worker>();
            }
            );
    }
}
