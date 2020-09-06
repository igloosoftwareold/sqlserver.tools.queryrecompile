:setvar MinQueryMicrosecond "999999"

IF EXISTS 
(
	SELECT *
	FROM sys.server_event_sessions
	WHERE name = 'QueriesOverOneSecond_Loop'
)
BEGIN
	DROP EVENT SESSION [QueriesOverOneSecond_Loop] ON SERVER 
END

CREATE EVENT SESSION [QueriesOverOneSecond_Loop] ON SERVER 
ADD EVENT sqlserver.module_end(
    ACTION(sqlos.task_time,sqlserver.client_app_name,sqlserver.client_hostname,sqlserver.database_id,sqlserver.database_name,sqlserver.query_hash,sqlserver.server_instance_name,sqlserver.session_id,sqlserver.sql_text)
    WHERE ([package0].[greater_than_uint64]([sqlserver].[database_id],(4)) AND [package0].[equal_boolean]([sqlserver].[is_system],(0)) AND [duration]>=($(MinQueryMicrosecond)))),
ADD EVENT sqlserver.rpc_completed(SET collect_statement=(1)
    ACTION(sqlos.task_time,sqlserver.client_app_name,sqlserver.client_hostname,sqlserver.database_id,sqlserver.database_name,sqlserver.query_hash,sqlserver.server_instance_name,sqlserver.session_id,sqlserver.sql_text)
    WHERE ([package0].[greater_than_uint64]([sqlserver].[database_id],(4)) AND [package0].[equal_boolean]([sqlserver].[is_system],(0)) AND [duration]>=($(MinQueryMicrosecond))))
ADD TARGET package0.ring_buffer(SET max_memory=(262144))
WITH (MAX_MEMORY=4096 KB,EVENT_RETENTION_MODE=ALLOW_SINGLE_EVENT_LOSS,MAX_DISPATCH_LATENCY=30 SECONDS,MAX_EVENT_SIZE=0 KB,MEMORY_PARTITION_MODE=NONE,TRACK_CAUSALITY=ON,STARTUP_STATE=ON)
GO

IF EXISTS 
(
	SELECT *
	FROM sys.server_event_sessions
	WHERE name = 'QueriesOverOneSecond_Loop'
)
BEGIN
	ALTER EVENT SESSION [QueriesOverOneSecond_Loop] ON SERVER STATE = START;
END
ELSE
BEGIN
	PRINT 'There was an error starting the Event Session'
END

