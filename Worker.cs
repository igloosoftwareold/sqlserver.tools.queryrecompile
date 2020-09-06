using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.XEvent.XELite;
using sqlserver.tools.queryrecompile.Models;

namespace sqlserver.tools.queryrecompile
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IOptions<DatabaseProcOptions> _databaseProcOptions;
        private readonly string _connectionString;
        private readonly string _sessionName;
        private readonly List<KeyValuePair<string, string>> _DatabasesAndProcs = new List<KeyValuePair<string, string>>(10);

        public Worker(ILogger<Worker> logger, IConfiguration configuration, IOptions<DatabaseProcOptions> databaseProcOptions)
        {
            _logger = logger;
            _configuration = configuration;
            _databaseProcOptions = databaseProcOptions;
            _connectionString = _configuration.GetConnectionString("DefaultConnection"); //;@"Server=d-sqlca1;Database=master;Trusted_Connection=True;";
            _sessionName = _configuration.GetValue<string>("XelSessionName");//;"QueriesOverOneSecond_Loop";

            foreach (var _databaseProcOption in _databaseProcOptions.Value.DatabaseProcTemplates.ToList())
            {
                _DatabasesAndProcs.Add(new KeyValuePair<string, string>(_databaseProcOption.DatabaseName, _databaseProcOption.ObjectName));
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("ExtendedEventsStreamConsumer running at: {time}", DateTimeOffset.Now);
                await ProcessXelStream();
                await Task.Delay(1000, stoppingToken);
            }
        }

        private static string GetFormattedDateTime()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        }

        private Task ProcessXelStream()
        {
            var cancellationTokenSource = new CancellationTokenSource();
            _logger.LogInformation($"\t\t\t\tStart Process XEL File:\t{GetFormattedDateTime()}");
            List<IXEvent> xEvent = new List<IXEvent>();
            List<XEventCustom> xEventCustoms = new List<XEventCustom>();

            var xeStream = new XELiveEventStreamer(_connectionString, _sessionName);

            Task waitTask = Task.Run(() =>
            {
                _logger.LogInformation("Press any key to stop listening...");
                Console.ReadKey();
                _logger.LogInformation($"\t\t\t\tStop Process XEL File:\t{GetFormattedDateTime()}\n");
                cancellationTokenSource.Cancel();
            });

            Task readTask = xeStream.ReadEventStream(() =>
            {
                _logger.LogInformation("Connected to session");
                return Task.CompletedTask;
            },
                xevent =>
                {
                    var syncTask = new Task(() => {
                        ProcessXEvent(xevent);
                    });
                    syncTask.RunSynchronously();
                    return Task.CompletedTask;
                },
                cancellationTokenSource.Token);


            try
            {
                Task.WaitAny(waitTask, readTask);
            }
            catch (TaskCanceledException)
            {
            }

            if (readTask.IsFaulted)
            {
                Console.Error.WriteLine("Failed with: {0}", readTask.Exception);
            }
            return Task.CompletedTask;
        }

        private Task ProcessXEvent(IXEvent xEvent)
        {
            var xeName = xEvent.Name;

            var xeTimestamp = xEvent.Timestamp;
            String xeObjectName = xEvent.Fields["object_name"].ToString();

            //TODO: Command Line Option to toggle this
            //String xeStatementField = (String)xevent.Fields["statement"].ToString();//We ignore the Statement because it'd make this very large.
            int xeDatabaseId = XEvent_GetDatabaseId(xEvent);
            ulong xeDurationField = XEvent_GetDurationField(xEvent);
            string xeClientAppNameAction = XEvent_GetClientAppNameAction(xEvent);
            double DurationField_Seconds = XEvent_GetDurationField_Seconds(xeDurationField);
            string DatabaseName = XEvent_GetDatabaseName(xEvent);

            if (_DatabasesAndProcs.Where(kvp => kvp.Key.ToUpper() == DatabaseName.ToUpper() && kvp.Value.ToUpper() == xeObjectName.ToUpper()).Any())
            {
                RecompileQuery(xeObjectName, xeClientAppNameAction, DurationField_Seconds, DatabaseName);
#if DEBUG
                {

                    //XEvent_PrintDebug(xeName, xeTimestamp, xeObjectName, xeDatabaseId, xeDurationField, xeClientAppNameAction, DurationField_Seconds, DatabaseName);
                }
#endif
            }
            //return xEventCustoms;
            return Task.CompletedTask;
        }

        private Dictionary<string, RecompileCounter> CurrentCounters = new Dictionary<string, RecompileCounter>();
        private void RecompileQuery(string xeObjectName, string xeClientAppNameAction, double DurationField_Seconds, string DatabaseName)
        {
            string CounterKey = $"{DatabaseName}:{xeObjectName}";

            if (CurrentCounters.ContainsKey(CounterKey))
            {
                CurrentCounters[CounterKey].NextValue();
            }
            else
            {
                CurrentCounters.Add(CounterKey, new RecompileCounter());
            }

            if (CurrentCounters[CounterKey].CanQueryBeRecompiled() || CurrentCounters[CounterKey].HasThresholdPassed())
            {
                _logger.LogInformation($"{GetFormattedDateTime()}: Recompiling: DatabaseName: {DatabaseName}, ObjectName: {xeObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {xeClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}");
                string xp_logevent_stored_proc = "[master].[dbo].xp_logevent";
                var xp_logevent_parameters = new DynamicParameters();
                int xp_logevent_error_number = 313377;
                string xp_logevent_message = $"Recompiling: DatabaseName: {DatabaseName}, ObjectName: {xeObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {xeClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}";

                xp_logevent_parameters.Add("@error_number", xp_logevent_error_number, DbType.Int32, ParameterDirection.Input);
                xp_logevent_parameters.Add("@message", xp_logevent_message, DbType.String, ParameterDirection.Input, xp_logevent_message.Length);


                string sp_recompile_stored_proc = "[dbo].sp_recompile";
                var sp_recompile_parameters = new DynamicParameters();

                var sp_recompile_objectName = xeObjectName;

                sp_recompile_parameters.Add("@objname", sp_recompile_objectName, DbType.String, ParameterDirection.Input, sp_recompile_objectName.Length);

                using (var connection = new SqlConnection(_connectionString))
                {
                    //We want to change the database context, the connection should be open now.
                    if (connection.State != ConnectionState.Open) connection.Open();
                    connection.ChangeDatabase(DatabaseName);

                    //This fires off the master database
                    _ = connection.Query(xp_logevent_stored_proc, xp_logevent_parameters, commandType: System.Data.CommandType.StoredProcedure).SingleOrDefault();
                    _ = connection.Query(sp_recompile_stored_proc, sp_recompile_parameters, commandType: System.Data.CommandType.StoredProcedure).SingleOrDefault();
                }
            }
            else
            {
                _logger.LogInformation($"\t{GetFormattedDateTime()}: DatabaseName: {DatabaseName}, ObjectName: {xeObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {xeClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}");
            }
        }

        private double XEvent_GetDurationField_Seconds(ulong xeDurationField)
        {
            return ((int)xeDurationField) / 1000.00 / 1000.00;
        }

        private string XEvent_GetClientAppNameAction(IXEvent xEvent)
        {
            return xEvent.Actions["client_app_name"].ToString();
        }

        private ulong XEvent_GetDurationField(IXEvent xEvent)
        {
            return (ulong)xEvent.Fields["duration"];
        }

        private int XEvent_GetDatabaseId(IXEvent xEvent)
        {
            return (ushort)xEvent.Actions["database_id"];
        }

        private void XEvent_PrintDebug(string xeName, DateTimeOffset xeTimestamp, string xeObjectName, int xeDatabaseId, ulong xeDurationField, string xeClientAppNameAction, double DurationField_Seconds, string DatabaseName)
        {
            _logger.LogInformation(
                $"EventName: {xeName}\t\n" +
                $"\tDatabase Id:\t\t{xeDatabaseId}\n" +
                $"\tDatabase Name:\t\t{DatabaseName}\n" +
                $"\tObject Name:\t\t{xeObjectName}\n" +
                $"\tTimestamp:\t\t{xeTimestamp.UtcDateTime.ToLocalTime().ToString("u").Replace("Z", "")}\n" +
                //$"\tStatement:\t\t{xeStatementField}\n" +//TODO: Command Line Option to toggle this
                $"\tDuration (NaNo):\t{xeDurationField}\n" +
                $"\tDuration (Sec):\t\t{DurationField_Seconds}\n" +
                $"\tClientAppName:\t\t{xeClientAppNameAction}\n"
                );
        }

        private XEventCustom XEvent_AddEvent(ref List<XEventCustom> xEventCustoms, DateTimeOffset xeTimestamp, string xeObjectName, string xeClientAppNameAction, double DurationField_Seconds, string DatabaseName)
        {
            var xEventCustom = new XEventCustom()
            {
                //EventName = xeName,//TODO: Command Line Option to toggle this
                //DatabaseId = xeDatabaseId,//TODO: Command Line Option to toggle this
                DatabaseName = DatabaseName,
                ObjectName = xeObjectName,
                Timestamp = xeTimestamp.UtcDateTime.ToLocalTime().ToString("u").Replace("Z", ""),//This date format so Excel can read it: 2020-05-13 00:00
                                                                                                 //DurationNano = xeDurationField,//TODO: Command Line Option to toggle this
                DurationSec = DurationField_Seconds,
                ClientAppName = xeClientAppNameAction
            };
            xEventCustoms.Add(xEventCustom);
            return xEventCustom;
        }

        private string XEvent_GetDatabaseName(IXEvent xEvent)
        {
            string DatabaseName;

            if (xEvent != null && xEvent.Actions != null && xEvent.Actions.ContainsKey("database_name") && xEvent.Actions["database_name"] != null)
            {
                DatabaseName = xEvent.Actions["database_name"].ToString();
            }
            else
            {
                DatabaseName = "NotResolved";
            }

            return DatabaseName;
        }
        public override void Dispose()
        {
            // DO YOUR STUFF HERE
        }
    }
}
