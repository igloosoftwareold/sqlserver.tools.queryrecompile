using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.XEvent.XELite;
using sqlserver.tools.queryrecompile.Models;
using System.Data;

namespace App.WindowsService;
public class RecompileService
{

    //private readonly ILogger<Worker> _logger;
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly IOptions<DatabaseProcOptions> _databaseProcOptions;
    private readonly string _connectionString;
    private readonly string _sessionName;
    private readonly List<KeyValuePair<string, string>> _DatabasesAndProcs = new(10);
    public RecompileService(ILogger<WindowsBackgroundService> logger, IConfiguration configuration, IOptions<DatabaseProcOptions> databaseProcOptions)
    {
        _logger = logger;
        _configuration = configuration;
        _databaseProcOptions = databaseProcOptions;
        _connectionString = _configuration.GetConnectionString("DefaultConnection"); //;@"Server=d-sqlca1;Database=master;Trusted_Connection=True;";
        _sessionName = _configuration.GetValue<string>("XelSessionName");//;"QueriesOverOneSecond_Loop";

        foreach (DatabaseProcTemplate? _databaseProcOption in _databaseProcOptions.Value.DatabaseProcTemplates.ToList())
        {
            if (_databaseProcOption.DatabaseName is string _databaseProcOption_DatabaseName && _databaseProcOption.ObjectName is string _databaseProcOption_ObjectName)
            {
                _DatabasesAndProcs.Add(new KeyValuePair<string, string>(_databaseProcOption_DatabaseName, _databaseProcOption_ObjectName));
            }
        }
    }

    private static string GetFormattedDateTime()
    {
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task ProcessXelStream(CancellationToken stoppingToken)
    {
        _logger.LogInformation("\t\t\t\tStart Process XEL File:\t{GetFormattedDateTime()}", GetFormattedDateTime());
        List<IXEvent> xEvent = new();
        List<XEventCustom> xEventCustoms = new();

        XELiveEventStreamer? xeStream = new(_connectionString, _sessionName);

        Task readTask = xeStream.ReadEventStream(() =>
        {
            _logger.LogInformation("Connected to session");
            return Task.CompletedTask;
        },
            xevent =>
            {
                Task? syncTask = new(() =>
                {
                    ProcessXEvent(xevent);
                });
                syncTask.RunSynchronously();
                return Task.CompletedTask;
            },
            stoppingToken);


        try
        {
            Task.WaitAny(new Task[] { readTask }, cancellationToken: stoppingToken);
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
        string? xeName = xEvent.Name;

        DateTimeOffset xeTimestamp = xEvent.Timestamp;
        if (IsIXEventFieldsValidAndValueNotNull(xEvent, "object_name", out string _xeObjectName))
        {
            string xeObjectName = _xeObjectName;

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
            }

            if (_databaseProcOptions.Value.RecompileQueriesNotOnList)
            {
                int QueryCountLimitNotOnList = (_databaseProcOptions != null && _databaseProcOptions.Value != null) ? _databaseProcOptions.Value.QueryCountLimitNotOnList : 2;

                RecompileQuery(xeClientAppNameAction, DurationField_Seconds, DatabaseName, xeObjectName, xeDatabaseId, xeObjectId, OnTheList, QueryCountLimitNotOnList);
            }
        }
        return Task.CompletedTask;
    }

    private static bool IsIXEventFieldsValidAndValueNotNull(IXEvent xEvent, string keyName, out string xeObjectValue)
    {
        xeObjectValue = "";
        if (xEvent != null && xEvent.Fields != null && xEvent.Fields.ContainsKey(keyName) && xEvent.Fields[keyName] != null && xEvent.Fields[keyName] is string _xeObjectName)
        {
            xeObjectValue = _xeObjectName;
            return true;
        }
        return false;
    }
    private static bool IsIXEventActionsValidAndValueNotNull(IXEvent xEvent, string keyName, out string xeObjectValue)
    {
        xeObjectValue = "";
        if (xEvent != null && xEvent.Actions != null && xEvent.Actions.ContainsKey(keyName) && xEvent.Actions[keyName] != null && xEvent.Actions[keyName] is string _xeObjectName)
        {
            xeObjectValue = _xeObjectName;
            return true;
        }
        return false;
    }

    private readonly Dictionary<string, RecompileCounter> CurrentCounters = new();
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
            using SqlConnection connection = new(_connectionString);
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            connection.ChangeDatabase(DatabaseName);

            /*
             * Setup and Get OBJECT_SCHEMA_NAME Parameters
            */
            string SchemaName = "dbo";
            string sp_OBJECT_SCHEMA_NAME_query = "SELECT OBJECT_SCHEMA_NAME(@object_id, @database_id) AS SchemaName";
            DynamicParameters sp_OBJECT_SCHEMA_NAME_parameters = new();
            sp_OBJECT_SCHEMA_NAME_parameters.Add("@object_id", ObjectId, DbType.Int32, ParameterDirection.Input);
            sp_OBJECT_SCHEMA_NAME_parameters.Add("@database_id", DatabaseId, DbType.Int32, ParameterDirection.Input);

            string? sp_OBJECT_SCHEMA_NAME_stored_proc_result = connection.Query<string>(sp_OBJECT_SCHEMA_NAME_query, sp_OBJECT_SCHEMA_NAME_parameters, commandType: System.Data.CommandType.Text).SingleOrDefault();

            if (sp_OBJECT_SCHEMA_NAME_stored_proc_result != null && sp_OBJECT_SCHEMA_NAME_stored_proc_result.Length > 0)
            {
                SchemaName = sp_OBJECT_SCHEMA_NAME_stored_proc_result;
            }

            /*
             * Setup xp_logevent parameters
            */
            string xp_logevent_stored_proc = "[master].[dbo].[xp_logevent]";
            DynamicParameters xp_logevent_parameters = new();
            int xp_logevent_error_number = 313377;
            string xp_logevent_message = $"Recompiling: DatabaseName: {DatabaseName}, ObjectName: {ObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {ClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}";

            xp_logevent_parameters.Add("@error_number", xp_logevent_error_number, DbType.Int32, ParameterDirection.Input);
            xp_logevent_parameters.Add("@message", xp_logevent_message, DbType.String, ParameterDirection.Input, xp_logevent_message.Length);

            /*
             * Setup sp_recompile parameters
            */
            string sp_recompile_stored_proc = "[dbo].[sp_recompile]";

            string? sp_recompile_objectNameFull = GetSp_recompile_objectNameFull(ObjectName, SchemaName);

            DynamicParameters? sp_recompile_parameters = new();
            sp_recompile_parameters.Add("@objname", sp_recompile_objectNameFull, DbType.String, ParameterDirection.Input, sp_recompile_objectNameFull.Length);
            //We want to change the database context, the connection should be open now.

            try
            {
                _ = connection.Query(xp_logevent_stored_proc, xp_logevent_parameters, commandType: System.Data.CommandType.StoredProcedure).SingleOrDefault();
                _ = connection.Query(sp_recompile_stored_proc, sp_recompile_parameters, commandType: System.Data.CommandType.StoredProcedure).SingleOrDefault();
                _logger.LogInformation("{GetFormattedDateTime()}: Recompiled: DatabaseName: {DatabaseName}, SchemaName: {SchemaName}, ObjectName: {ObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {ClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}", GetFormattedDateTime(), DatabaseName, SchemaName, ObjectName, DurationField_Seconds, ClientAppNameAction, CurrentCounters[CounterKey].QueryThreshold, CurrentCounters[CounterKey].GetValue());
            }
            catch
            {
                _logger.LogError("{GetFormattedDateTime()}: Error: DatabaseName: {DatabaseName}, SchemaName: {SchemaName}, ObjectName: {ObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {ClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}", GetFormattedDateTime(), DatabaseName, SchemaName, ObjectName, DurationField_Seconds, ClientAppNameAction, CurrentCounters[CounterKey].QueryThreshold, CurrentCounters[CounterKey].GetValue());
            }
        }
        else
        {
            _logger.LogInformation("\t{GetFormattedDateTime()}: DatabaseName: {DatabaseName}, ObjectName: {ObjectName}, Duration: {DurationField_Seconds:0.00}, Client App: {ClientAppNameAction}, How Many In Last {CurrentCounters[CounterKey].QueryThreshold} Seconds: {CurrentCounters[CounterKey].GetValue()}", GetFormattedDateTime(), DatabaseName, ObjectName, DurationField_Seconds, ClientAppNameAction, CurrentCounters[CounterKey].QueryThreshold, CurrentCounters[CounterKey].GetValue());
        }
    }

    private static string GetSp_recompile_objectNameFull(string ObjectName, string SchemaName)
    {
        return '[' + SchemaName + "].[" + ObjectName + ']';
    }

    private static double XEvent_GetDurationField_Seconds(ulong xeDurationField)
    {
        return ((int)xeDurationField) / 1000.00 / 1000.00;
    }

    private static string XEvent_GetClientAppNameAction(IXEvent xEvent)
    {
        if (IsIXEventActionsValidAndValueNotNull(xEvent, "client_app_name", out string _xeObjectName))
        {
            return _xeObjectName;
        }
        else
        {
            return "";
        }
    }

    private static ulong XEvent_GetDurationField(IXEvent xEvent)
    {
        return xEvent.Fields.ContainsKey("duration") ? (ulong)xEvent.Fields["duration"] : 0;
    }

    private static int XEvent_GetObjectId(IXEvent xEvent)
    {
        return xEvent.Fields.ContainsKey("object_id") ? (int)xEvent.Fields["object_id"] : 0;
    }

    private static int XEvent_GetDatabaseId(IXEvent xEvent)
    {
        return xEvent.Actions.ContainsKey("database_id") ? (ushort)xEvent.Actions["database_id"] : 0;
    }

    private static string XEvent_GetDatabaseName(IXEvent? xEvent)
    {
        string? DatabaseName;
        if (xEvent != null && xEvent.Actions != null && xEvent.Actions.ContainsKey("database_name") && xEvent.Actions["database_name"] != null && xEvent.Actions["database_name"] is string _databaseName)
        {
            DatabaseName = _databaseName;
        }
        else
        {
            DatabaseName = "NotResolved";
        }

        return DatabaseName;
    }
}