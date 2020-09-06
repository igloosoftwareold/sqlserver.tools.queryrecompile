:setvar DatabaseName LongQueries

USE $(DatabaseName)
GO

IF  EXISTS (SELECT * FROM sys.schemas WHERE name = N'notdbo')
BEGIN
	DROP SCHEMA [notdbo]
END
GO

CREATE SCHEMA [notdbo]
GO


