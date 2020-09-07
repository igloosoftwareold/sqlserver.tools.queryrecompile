# sqlserver.tools.queryrecompile
A tool that automatically recompiles Queries over a certain threshold.  The Threshold is currently controlled in the extended events file.

Add the "QueriesOverOneSecond_Loop" to your SQL Server (.\Extra\ExtendedEvents\QueriesOverOneSecond_Loop.sql)

Configure your SQL Server in the appsettings.json file, use appsettings.Development.json as a template.

**ConnectionStrings**
Add your Database Server here and any additional connection string information.
```
  "ConnectionStrings": {
    "DefaultConnection": "Server=databaseServer;Database=master;Trusted_Connection=True;"
  }
```

**XelSessionName**
If you've changed the name of the XelSessionName from the default make sure to change it here.
```
  "XelSessionName": "QueriesOverOneSecond_Loop",
```
```
  "DatabaseQueryThreshold": {
    "RecompileQueriesNotOnList": "true",
    "QueryCountLimitNotOnList": 2,
    "DatabaseProcTemplates": [
      {
        "DatabaseName": "LongQueries",
        "SchemaName": "dbo",
        "ObjectName": "DelayMe"
      },
      {
        "DatabaseName": "LongQueries",
        "SchemaName": "notdbo",
        "ObjectName": "NotDbo_delayme"
      }
    ]
  }
```
**RecompileQueriesNotOnList**
True/False: Enable Recompile for Queries not on the list

**QueryCountLimitNotOnList**
Integer: How many times a query can occur in 35 seconds before it triggers a recompile.

**DatabaseName**
The Database Name that you want to filter by.

**SchemaName**
This currently is just for show.

**ObjectName**
The ObjectName to filter by.

**Example output**
```
info: sqlserver.tools.queryrecompile.Worker[0]
      ExtendedEventsStreamConsumer running at: 09/06/2020 17:54:41 -04:00
info: sqlserver.tools.queryrecompile.Worker[0]
                                Start Process XEL File: 2020-09-06 17:54:41.813
info: sqlserver.tools.queryrecompile.Worker[0]
      Press any key to stop listening...
info: sqlserver.tools.queryrecompile.Worker[0]
      Connected to session
info: sqlserver.tools.queryrecompile.Worker[0]
      2020-09-06 17:54:50.789: Recompiled: DatabaseName: LongQueries, SchemaName: dbo, ObjectName: DelayMe, Duration: 1.00, Client App: Microsoft SQL Server Management Studio - Query, How Many In Last 35 Seconds: 0
info: sqlserver.tools.queryrecompile.Worker[0]
      2020-09-06 17:54:50.915: Recompiled: DatabaseName: LongQueries, SchemaName: notdbo, ObjectName: NotDbo_DelayMe, Duration: 1.00, Client App: Microsoft SQL Server Management Studio - Query, How Many In Last 35 Seconds: 0
info: sqlserver.tools.queryrecompile.Worker[0]
```

