:setvar DatabaseName LongQueries

USE $(DatabaseName)
GO

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DelayMe]') AND type in (N'P', N'PC'))
BEGIN
	DROP PROCEDURE [dbo].[DelayMe]
END
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[DelayMe]
	@DelayText VARCHAR(255)
AS
BEGIN
	SET NOCOUNT ON;
	waitfor delay @DelayText
END
GO

/*
Create a stored procedure in a different schema to verify we can recompile it.
*/

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[notdbo].[NotDbo_DelayMe]') AND type in (N'P', N'PC'))
BEGIN
	DROP PROCEDURE [notdbo].[NotDbo_DelayMe]
END
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [notdbo].[NotDbo_DelayMe]
	@DelayText VARCHAR(255)
AS
BEGIN
	SET NOCOUNT ON;
	waitfor delay @DelayText
END
GO

