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
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Service");
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _logger.LogInformation("Disposing Service");
            base.Dispose();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("ExtendedEventsStreamConsumer running at: {time}", DateTimeOffset.Now);
                await ProcessXelStream(stoppingToken);
                await Task.Delay(1000, stoppingToken);
            }
        }

        private static string GetFormattedDateTime()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        }

        private Task ProcessXelStream(CancellationToken stoppingToken)
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
                _logger.LogInformation("Cancellation Token Requested");
                cancellationTokenSource.Cancel();
            });

            Task readTask = xeStream.ReadEventStream(() =>
            {
                _logger.LogInformation("Connected to session");
                return Task.CompletedTask;
            },
                xevent =>
                {
                    var syncTask = new Task(() =>
                    {
                        ProcessXEvent(xevent);
                    });
                    syncTask.RunSynchronously();
                    return Task.CompletedTask;
                },
                cancellationTokenSource.Token);


            try
            {
                Task.WaitAny(readTask, waitTask);
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
            int xeObjectId = XEvent_GetObjectId(xEvent);

            ulong xeDurationField = XEvent_GetDurationField(xEvent);
            
            string xeClientAppNameAction = XEvent_GetClientAppNameAction(xEvent);
            double DurationField_Seconds = XEvent_GetDurationField_Seconds(xeDurationField);
            string DatabaseName = XEvent_GetDatabaseName(xEvent);

            bool OnTheList = false;
            if (_DatabasesAndProcs.Where(kvp => kvp.Key.ToUpper() == DatabaseName.ToUpper() && kvp.Value.ToUpper() == xeObjectName.ToUpper()).Any())
            {
                OnTheList = true;
#if DEBUG
                {

                    //XEvent_PrintDebug(xeName, xeTimestamp, xeObjectName, xeDatabaseId, xeDurationField, xeClientAppNameAction, DurationField_Seconds, DatabaseName);
                }
#endif
            }

            if (_databaseProcOptions.Value.RecompileQueriesNotOnList)
            {
                int QueryCountLimitNotOnList = (_databaseProcOptions != null && _databaseProcOptions.Value != null) ? _databaseProcOptions.Value.QueryCountLimitNotOnList : 2;
                
                RecompileQuery(xeClientAppNameAction, DurationField_Seconds, DatabaseName, xeObjectName, xeDatabaseId, xeObjectId, OnTheList, QueryCountLimitNotOnList);
            }
            
            //return xEventCustoms;
            return Task.CompletedTask;
        }

        private readonly Dictionary<string, RecompileCounter> CurrentCounters = new Dictionary<string, RecompileCounter>();
        private void RecompileQuery(string ClientAppNameAction, double DurationField_Seconds, string DatabaseName, string ObjectName, int DatabaseId, int ObjectId, bool OnTheList = false, int QueryCountLimitNotOnList = 2)
        {
            string CounterKey = $"{DatabaseName}:{ObjectName}";

            if (CurrentCounters.ContainsKey(CounterKey))
            {
                CurrentCounters[CounterKey].NextValue();
            }
            else
            {
                //This initializes the value to 0.
                CurrentCounters.Add(CounterKey, new RecompileCounter());

                if (!OnTheList)
                {
                    //When we initialize a query that's not on the list to be recompiled, we set the counter to 1 instead of 0.
                    CurrentCounters[CounterKey].NextValue();
                }
            }

            if (
                CurrentCounters[CounterKey].CanQueryBeRecompiled(OnTheList, QueryCountLimitNotOnList) || CurrentCounters[CounterKey].HasThresholdPassed()
            )
            {
                using SqlConnection connection = new SqlConnection(_connectionString);
                if (connection.State != ConnectionState.Open) connection.Open();
                connection.ChangeDatabase(DatabaseName);

                /*
                 * Setup and Get OBJECT_SCHEMA_NAME Parameters
                */
                string SchemaName = "dbo";
                string sp_OBJECT_SCHEMA_NAME_query = "SELECT OBJECT_SCHEMA_NAME(@object_id, @database_id) AS SchemaName";
                var sp_OBJECT_SCHEMA_NAME_parameters = new DynamicParameters();
                sp_OBJECT_SCHEMA_NAME_parameters.Add("@object_id", ObjectId, DbType.Int32, ParameterDirection.Input);
                sp_OBJECT_SCHEMA_NAME_parameters.Add("@database_id", DatabaseId, DbType.Int32, ParameterDirection.Input);

                string sp_OBJECT_SCHEMA_NAME_stored_proc_result = connection.Query<string>(sp_OBJECT_SCHEMA_NAME_query, sp_OBJECT_SCHEMA_NAME_parameters, commandType: System.Data.CommandType.Text).SingleOrDefault();

                if (sp_OBJECT_SCHEMA_NAME_stored_proc_result != null && sp_OBJECT_SCHEMA_NAME_stored_proc_result.Length > 0)
                {
                    SchemaName = sp_OBJECT_SCHEMA_NAME_stored_proc_result;
                }

                /*
                 * Setup xp_logevent parameters
                */
                string xp_logevent_stored_proc = "[master].[dbo].[xp_logevent]";
                var xp_logevent_parameters = new DynamicParameters();
                int xp_logevent_error_number = 313377;
                string xp_logevent_message = $"Recompiling: DatabaseName: {DatabaseName}, ObjectName: {ObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {ClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}";

                xp_logevent_parameters.Add("@error_number", xp_logevent_error_number, DbType.Int32, ParameterDirection.Input);
                xp_logevent_parameters.Add("@message", xp_logevent_message, DbType.String, ParameterDirection.Input, xp_logevent_message.Length);

                /*
                 * Setup sp_recompile parameters
                */
                string sp_recompile_stored_proc = "[dbo].[sp_recompile]";

                var sp_recompile_objectNameFull = GetSp_recompile_objectNameFull(ObjectName, SchemaName);

                var sp_recompile_parameters = new DynamicParameters();
                sp_recompile_parameters.Add("@objname", sp_recompile_objectNameFull, DbType.String, ParameterDirection.Input, sp_recompile_objectNameFull.Length);
                //We want to change the database context, the connection should be open now.

                try
                {
                    _ = connection.Query(xp_logevent_stored_proc, xp_logevent_parameters, commandType: System.Data.CommandType.StoredProcedure).SingleOrDefault();
                    _ = connection.Query(sp_recompile_stored_proc, sp_recompile_parameters, commandType: System.Data.CommandType.StoredProcedure).SingleOrDefault();

                    _logger.LogInformation($"{GetFormattedDateTime()}: Recompiled: DatabaseName: {DatabaseName}, SchemaName: {SchemaName}, ObjectName: {ObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {ClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}");
                }
                catch
                {
                    _logger.LogError($"{GetFormattedDateTime()}: Error: DatabaseName: {DatabaseName}, SchemaName: {SchemaName}, ObjectName: {ObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {ClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}");
                }
            }
            else
            {
                _logger.LogInformation($"\t{GetFormattedDateTime()}: DatabaseName: {DatabaseName}, ObjectName: {ObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {ClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}");
            }
        }

        private static string GetSp_recompile_objectNameFull(string ObjectName, string SchemaName)
        {
            return '[' + SchemaName + "].[" + ObjectName + ']';
        }

        private double XEvent_GetDurationField_Seconds(ulong xeDurationField)
        {
            return ((int)xeDurationField) / 1000.00 / 1000.00;
        }

        private string XEvent_GetClientAppNameAction(IXEvent xEvent)
        {
            return xEvent.Actions.ContainsKey("client_app_name") ? xEvent.Actions["client_app_name"].ToString() : "";
        }

        private ulong XEvent_GetDurationField(IXEvent xEvent)
        {
            return xEvent.Fields.ContainsKey("duration") ? (ulong)xEvent.Fields["duration"] : 0;
        }

        private int XEvent_GetObjectId(IXEvent xEvent)
        {
            return xEvent.Fields.ContainsKey("object_id") ? (int)xEvent.Fields["object_id"] : 0;
        }

        private int XEvent_GetDatabaseId(IXEvent xEvent)
        {
            return xEvent.Actions.ContainsKey("database_id") ? (ushort)xEvent.Actions["database_id"] : 0;
        }

#if DEBUG
        /*
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
        */
#endif
        /*
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
        */

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
    }
}
